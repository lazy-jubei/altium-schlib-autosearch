using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows.Forms;
using DXP;
using RT_IntegratedLibrary;

[assembly: ComVisible(true)]
[assembly: AssemblyVersion("1.1.0.0")]

namespace CSharpPlugin {
 [ComVisible(true),ClassInterface(ClassInterfaceType.None)]
 public sealed class PluginFactory {
  public object InvokePluginFactory(IClient client) {
   string path=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Altium","SchLibSearchPaths");
   Directory.CreateDirectory(path);File.AppendAllText(Path.Combine(path,"activity.log"),DateTime.Now.ToString("s")+" Factory entered\n");
   return new SchLibSearchPaths.LibraryModule(client);
  }
 }
}
namespace SchLibSearchPaths {
 public sealed class LibraryModule:ServerModule {
  public const string Name="SchLibSearchPaths";
  readonly Timer timer;
  readonly IClient client;
  ServerPanelView panel;
  public LibraryModule(IClient client):base(client,Name) {
   this.client=client;
   Service.Initialize();
   client.AddServerView(CreateServerViewImpl(Name));
   timer=new Timer {Interval=2000};
   timer.Tick+=(s,e)=>Service.Tick();
   timer.Start();
  }
  protected override IServerDocument NewDocumentInstance(string kind,string filename)=>null;
  protected override IServerView CreateServerViewImpl(string name) {
   if(name!=Name)return null;
   if(panel==null){panel=new LibraryPanelView(new LibraryPanel());AddView(panel);}
   return panel;
  }
  protected override void InitializeCommands() {
   ((CommandLauncher)CommandLauncher).RegisterCommand("Refresh",new CommandProc(Refresh));
   ((CommandLauncher)CommandLauncher).RegisterCommand("Disable",new CommandProc(Disable));
   ((CommandLauncher)CommandLauncher).RegisterCommand("Status",new CommandProc(Status));
   ((CommandLauncher)CommandLauncher).RegisterCommand("ShowPanel",new CommandProc(ShowPanel));
  }
  void Refresh(IServerDocumentView v,ref string p){Service.Enable();Service.Sync(true);}
  void Disable(IServerDocumentView v,ref string p){Service.Disable();}
  void Status(IServerDocumentView v,ref string p){MessageBox.Show(Service.StatusText(),"Project Search Path Symbols");}
  void ShowPanel(IServerDocumentView v,ref string p){client.GetGUIManager().SetPanelVisibleInCurrentForm(Name,true);client.GetGUIManager().SetPanelActiveInCurrentForm(Name);}
 }
 public sealed class Settings {
  public bool Enabled {get;set;}=true;
  public List<string> OwnedLibraries {get;set;}=new();
 }
 public static class Service {
  static readonly string Folder=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Altium","SchLibSearchPaths");
  static readonly string StateFile=Path.Combine(Folder,"state.json");
  static readonly string LogFile=Path.Combine(Folder,"activity.log");
  static Settings state;
  static bool inside,initialized,optionChecked;
  static string lastFingerprint="",lastProject="",lastStatus="Waiting for a project";
  static DateTime nextScan=DateTime.MinValue;
  static readonly StringComparer cmp=StringComparer.OrdinalIgnoreCase;
  public static void Initialize() {
   if(initialized)return;
   Directory.CreateDirectory(Folder);
   if(File.Exists(StateFile)) {
    try {state=JsonSerializer.Deserialize<Settings>(File.ReadAllText(StateFile)) ?? throw new InvalidDataException("Empty state");}
    catch(Exception e){state=new Settings {Enabled=false};Log("Invalid ownership state; synchronization disabled: "+e.Message);}
   } else state=new Settings();
   state.OwnedLibraries=state.OwnedLibraries?.Distinct(cmp).ToList()??new();
   initialized=true;Log("Loaded; enabled="+state.Enabled+"; owned="+state.OwnedLibraries.Count);
  }
  public static bool Enabled {get{Initialize();return state.Enabled;}}
  public static string Summary {get{Initialize();return lastStatus;}}
  public static string ActiveProject => lastProject;
  public static void Enable(){Initialize();EnsureSearchPathsOption();state.Enabled=true;SaveState();lastFingerprint="";nextScan=DateTime.MinValue;}
  public static bool EnsureSearchPathsOption() {
   const string key="UI.ProjectOptions.SearchPaths";
   var client=Altium.Dxp.Interfaces.DxpImports.ClientApiGetClientInterface();
   var options=client?.GetInternalOptions();
   if(options==null||!options.OptionExists(key))return false;
   if(!options.ReadBoolean(key)){options.WriteBoolean(key,true);client.SetPreferencesChanged(true);Log("Enabled "+key);}
   optionChecked=options.ReadBoolean(key);return optionChecked;
  }
  public static void Tick() {
   Initialize();if(!state.Enabled||inside||DateTime.UtcNow<nextScan)return;
   var client=Altium.Dxp.Interfaces.DxpImports.ClientApiGetClientInterface();
   if(client==null||!client.IsInitialized()||client.IsQuitting()||client.GetProcessControl().GetProcessDepth()>0)return;
   nextScan=DateTime.UtcNow.AddSeconds(5);Sync(false);
  }
  static IIntegratedLibraryManager Manager() => (IIntegratedLibraryManager)typeof(IntegratedLibrary.GlobalVars).Assembly.GetType("EDP.Utils").GetMethod("LoadIntegratedLibraryManager").Invoke(null,null);
  static HashSet<string> Installed(IIntegratedLibraryManager manager) => new(Enumerable.Range(0,manager.InstalledLibraryCount()).Select(manager.InstalledLibraryPath),cmp);
  public static void Sync(bool force) {
   Initialize();if(inside||!state.Enabled)return;
   inside=true;var sw=Stopwatch.StartNew();
   try {
    if(!optionChecked)EnsureSearchPathsOption();
    var ws=EDP.GlobalVars.EDPWorkspace;
    var project=ws?.DM_FocusedProject();
    // Do not clear ownership during a transient focus change while projects load.
    if(project==null&&ws?.DM_ProjectCount()>0)return;
    string projectPath=project?.DM_ProjectFullPath()??"";
    // Startup and library editors can temporarily focus the Free Documents pseudo-project.
    // Preserve registrations until a real project becomes focused instead of remove/re-add churn.
    if(project!=null&&!Path.IsPathRooted(projectPath))return;
    var desired=new HashSet<string>(cmp);
    var specifications=new List<string>();
    if(project!=null)for(int i=0;i<project.DM_SearchPathCount();i++) {
     var entry=project.DM_SearchPaths(i);string absolute=entry.DM_AbsolutePath();
     string root=Path.GetDirectoryName(absolute),mask=Path.GetFileName(absolute);
     specifications.Add(absolute+"|"+entry.DM_IncludeSubFolders());
     if(!Directory.Exists(root))continue;
     foreach(string file in Directory.EnumerateFiles(root,mask,entry.DM_IncludeSubFolders()?SearchOption.AllDirectories:SearchOption.TopDirectoryOnly))
      if(Path.GetExtension(file).Equals(".SchLib",StringComparison.OrdinalIgnoreCase))desired.Add(Path.GetFullPath(file));
    }
    string fingerprint=projectPath+"\n"+string.Join("\n",specifications)+"\n"+string.Join("\n",desired.OrderBy(x=>x,cmp).Select(f=>f+"|"+File.GetLastWriteTimeUtc(f).Ticks+"|"+new FileInfo(f).Length));
    if(!force&&fingerprint==lastFingerprint)return;
    var manager=Manager();var reader=IntegratedLibrary.GlobalVars.LibraryDataReader;
    if(manager==null||reader==null)return;
    var installed=Installed(manager);
    int added=0,removed=0;
    foreach(string old in state.OwnedLibraries.Where(f=>!desired.Contains(f)).ToArray()) {
     if(installed.Contains(old)){manager.UninstallLibrary(old);removed++;}
     state.OwnedLibraries.RemoveAll(f=>cmp.Equals(f,old));SaveState();
    }
    var available=new HashSet<string>(Enumerable.Range(0,manager.AvailableLibraryCount()).Select(manager.AvailableLibraryPath),cmp);
    foreach(string file in desired.OrderBy(f=>f,cmp)) {
     if(installed.Contains(file)||available.Contains(file))continue;
     // Persist ownership before installing, so a crash cannot leave untracked additions.
     if(!state.OwnedLibraries.Contains(file,cmp))state.OwnedLibraries.Add(file);
     SaveState();
     var data=reader.RegisterLibrary(file);
     if(data==null)throw new InvalidOperationException("Library registration failed: "+file);
     installed.Add(file);available.Add(file);added++;
     if(added%25==0)Log("Progress: registered "+added+" libraries");
    }
    lastProject=projectPath;lastFingerprint=fingerprint;
    lastStatus=desired.Count+" schematic libraries found; "+state.OwnedLibraries.Count+" managed by this extension.";
    Log("Synchronized "+projectPath+"; found="+desired.Count+"; added="+added+"; removed="+removed+"; installed="+manager.InstalledLibraryCount()+"; milliseconds="+sw.ElapsedMilliseconds);
    // One queued refresh also makes edits to already indexed libraries visible.
    string parameters="";
    DXP.Utils.MessageRouterPostCommandToModule("IntegratedLibrary:RefreshInstalledLibraries",ref parameters,null);
   }catch(Exception e){lastStatus="Synchronization failed: "+e.Message;Log(e.ToString());nextScan=DateTime.UtcNow.AddSeconds(30);}
   finally{inside=false;}
  }
  public static void Disable() {
   Initialize();if(inside)throw new InvalidOperationException("A synchronization is in progress");
   state.Enabled=false;SaveState();inside=true;
   // Leave Altium's Search Paths tab available; do not undo a user's preference.
   try {
    var manager=Manager();var installed=Installed(manager);
    foreach(string file in state.OwnedLibraries.ToArray()) {
     if(installed.Contains(file))manager.UninstallLibrary(file);
     state.OwnedLibraries.RemoveAll(f=>cmp.Equals(f,file));SaveState();
    }
    lastFingerprint="";lastStatus="Disabled; extension-owned library registrations removed.";
    Log(lastStatus);
    string parameters="";DXP.Utils.MessageRouterPostCommandToModule("IntegratedLibrary:RefreshInstalledLibraries",ref parameters,null);
   }catch(Exception e){Log(e.ToString());throw;}
   finally{inside=false;}
  }
  public static string StatusText(){Initialize();return "Enabled: "+state.Enabled+"\nProject: "+lastProject+"\n"+lastStatus+"\n\nReads the existing Project Options > Search Paths. Original library files are not modified.\nLog: "+LogFile;}
  static void SaveState() {
   string temp=StateFile+".new";File.WriteAllText(temp,JsonSerializer.Serialize(state,new JsonSerializerOptions{WriteIndented=true}));File.Move(temp,StateFile,true);
  }
  static void Log(string text){try{File.AppendAllText(LogFile,DateTime.Now.ToString("s")+" "+text+Environment.NewLine);}catch{}}
 }
}
