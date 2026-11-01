using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using System.Xml.Linq;
using System.Xml;
using Microsoft.Win32;

[assembly:AssemblyTitle("Library Search Paths Setup")]
[assembly:AssemblyVersion("1.1.0.0")]
[assembly:AssemblyFileVersion("1.1.0.0")]
namespace LibrarySearchPathsSetup {
 internal sealed class Target {
  public string RegistryPath,DisplayName;
  public override string ToString(){return DisplayName;}
 }
 internal static class Engine {
  internal const string Hrid="SchLibSearchPaths",Version="1.1.0.0";
  internal static readonly string DefaultDestination=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Altium","SchLibSearchPaths");
  internal static readonly string[] Files={"SchLibSearchPaths.dll","SchLibSearchPaths.ins","Control.ps1","README.md"};
  internal static string Hash(byte[] data){using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(data)).Replace("-","");}
  internal static Dictionary<string,byte[]> Payload(){
   byte[] bytes;using(var stream=Assembly.GetExecutingAssembly().GetManifestResourceStream("payload.zip"))using(var memory=new MemoryStream()){stream.CopyTo(memory);bytes=memory.ToArray();}
   if(Hash(bytes)!=PayloadManifest.Sha256)throw new InvalidDataException("The embedded installation package failed its integrity check.");
   var result=new Dictionary<string,byte[]>(StringComparer.OrdinalIgnoreCase);
   using(var memory=new MemoryStream(bytes))using(var zip=new ZipArchive(memory,ZipArchiveMode.Read))foreach(var entry in zip.Entries){
    if(!Files.Contains(entry.FullName)||result.ContainsKey(entry.FullName))throw new InvalidDataException("Unexpected installation package entry.");
    using(var input=entry.Open())using(var output=new MemoryStream()){input.CopyTo(output);result.Add(entry.FullName,output.ToArray());}
   }
   if(result.Count!=Files.Length)throw new InvalidDataException("Incomplete installation package.");return result;
  }
  internal static List<Target> Discover(){
   var result=new List<Target>();
   foreach(var hive in new[]{RegistryHive.LocalMachine,RegistryHive.CurrentUser})foreach(var view in new[]{RegistryView.Registry64,RegistryView.Registry32}){
    using(var root=RegistryKey.OpenBaseKey(hive,view))using(var uninstall=root.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall")){
     if(uninstall==null)continue;
     foreach(string name in uninstall.GetSubKeyNames())using(var key=uninstall.OpenSubKey(name)){
      string display=Convert.ToString(key.GetValue("DisplayName")),version=Convert.ToString(key.GetValue("DisplayVersion")),location=Convert.ToString(key.GetValue("InstallLocation"));
      if(!display.StartsWith("Altium Designer",StringComparison.OrdinalIgnoreCase)||!name.Contains("{"))continue;
      System.Version parsed;if(!System.Version.TryParse(version,out parsed)||parsed.Major!=26)continue;
      string path=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),"Altium",name,"Extensions","ExtensionsRegistry.xml");
      if(File.Exists(path)&&File.Exists(Path.Combine(location,"System","Altium.Edp.Interfaces.dll"))&&!result.Any(t=>String.Equals(t.RegistryPath,path,StringComparison.OrdinalIgnoreCase)))result.Add(new Target{RegistryPath=path,DisplayName=display+" ("+version+")"});
     }
    }
   }
   return result;
  }
  internal static void RequireClosed(){if(Process.GetProcessesByName("X2").Length!=0)throw new InvalidOperationException("Save your work and close Altium, then try again. The installer will not close it for you.");}
  internal static bool HasOwnedLibraries(string destination){
   string path=Path.Combine(destination,"state.json");if(!File.Exists(path))return false;
   var state=new JavaScriptSerializer().DeserializeObject(File.ReadAllText(path)) as Dictionary<string,object>;
   if(state==null)throw new InvalidDataException("Could not read the extension ownership state.");
   object list;if(!state.TryGetValue("OwnedLibraries",out list))return false;
   var values=list as object[];if(values==null)throw new InvalidDataException("Invalid ownership list.");return values.Length>0;
  }
  internal static string Apply(Target target,string destination,bool remove){
   string full=Path.GetFullPath(destination);if(!Path.IsPathRooted(destination)||String.Equals(full,Path.GetPathRoot(full),StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Invalid installation directory.");
   var payload=Payload();var doc=XDocument.Load(target.RegistryPath);if(doc.Root==null||doc.Root.Name!="Extensions")throw new InvalidDataException("Invalid Altium extension registry.");
   if(remove&&HasOwnedLibraries(full))throw new InvalidOperationException("First open Altium's Library Search Paths panel and clear its checkbox. This removes the automatic registrations. Then close Altium and run Remove again.");
   // Altium discovers extension files recursively: keep old INS/DLL files outside the live folder.
   string backup=Path.Combine(Path.GetDirectoryName(full),Path.GetFileName(full)+"-InstallBackups",DateTime.Now.ToString("yyyyMMdd-HHmmss")+"-"+Guid.NewGuid().ToString("N").Substring(0,8));Directory.CreateDirectory(backup);
   string registryBackup=Path.Combine(backup,"ExtensionsRegistry.xml");File.Copy(target.RegistryPath,registryBackup);
   var existed=new HashSet<string>();foreach(string file in Files){string path=Path.Combine(full,file);if(File.Exists(path)){File.Copy(path,Path.Combine(backup,file));existed.Add(file);}}
   foreach(var item in doc.Root.Elements("Item").Where(e=>(string)e.Attribute("HRID")==Hrid).ToList())item.Remove();
   string temp=target.RegistryPath+".schlib-"+Guid.NewGuid().ToString("N")+".tmp";
   try {
    if(!remove){
     foreach(string file in Files)File.WriteAllBytes(Path.Combine(full,file),payload[file]);
     // The rejected experimental menu resource must never be loaded again.
     string oldMenu=Path.Combine(full,"SchLibSearchPaths.rcs");if(File.Exists(oldMenu))File.Move(oldMenu,Path.Combine(backup,"obsolete-menu.rcs.txt"));
     var item=new XElement("Item",new XAttribute("HRID",Hrid),new XAttribute("Guid","E3DE8765-9D4A-4E57-9CCF-82D6C9A1CEB4"));
     var fields=new Dictionary<string,string>{{"Path",full},{"Status","0"},{"VaultGuid",""},{"CreatedBy","Local extension"},{"CategoryGuid","793A1F67-0B22-4E01-A5DE-3176A1E8C60D"},{"CategoryName",""},{"ReadMe","README.md"},{"Help",""},{"Requirements","Altium Designer 26; tested on 26.11.1"},{"Title","Library Search Paths"},{"ShortDescription","Find SchLib files using existing project search paths."},{"LongDescription","Uses the original Project Options Search Paths. Includes an enable checkbox and enables the Search Paths tab."},{"SmallImage",""},{"LargeImage",""},{"Version",Version},{"VersionGuid","AE499EB6-3D56-419B-A92B-10D994D52935"},{"ReleasedDate",DateTime.Today.ToOADate().ToString(System.Globalization.CultureInfo.InvariantCulture)},{"ReleaseNotes","Single installer and in-Altium enable checkbox."},{"DateInstalled",DateTime.Today.ToOADate().ToString(System.Globalization.CultureInfo.InvariantCulture)}};
     foreach(var pair in fields)item.Add(new XElement(pair.Key,pair.Value));
     item.Add(new XElement("PlatformVersions",new XElement("DXP",new XAttribute("BuildNumber","1.0.16.43")),new XElement("EDP",new XAttribute("BuildNumber","1.0.16.43")),new XElement("MaxDXP",new XAttribute("BuildNumber","0.0.0.0")),new XElement("MaxEDP",new XAttribute("BuildNumber","0.0.0.0"))));doc.Root.Add(item);
    }
    var xmlSettings=new XmlWriterSettings {Indent=true,Encoding=new UTF8Encoding(false)};
    using(var writer=XmlWriter.Create(temp,xmlSettings))doc.Save(writer);
    File.Replace(temp,target.RegistryPath,null);
   }catch{
    if(File.Exists(temp))File.Delete(temp);
    if(!remove)foreach(string file in Files){string path=Path.Combine(full,file);if(existed.Contains(file))File.Copy(Path.Combine(backup,file),path,true);else if(File.Exists(path))File.Delete(path);}
    throw;
   }
   return (remove?"Extension removed. Files and settings were retained.":"Installed. Reopen Altium, then choose Panels > Library Search Paths.")+Environment.NewLine+"Backup: "+backup;
  }
  internal static void SelfTest(string folder){
   folder=Path.GetFullPath(folder);if(Directory.Exists(folder))throw new InvalidOperationException("The test folder must not already exist.");Directory.CreateDirectory(folder);
   string registry=Path.Combine(folder,"ExtensionsRegistry.xml"),destination=Path.Combine(folder,"installed");Directory.CreateDirectory(destination);
   File.WriteAllText(registry,"<Extensions><Item HRID=\"ExistingPlugin\"><Path>unchanged</Path></Item></Extensions>");
   File.WriteAllText(Path.Combine(destination,"state.json"),"{\"Enabled\":false,\"OwnedLibraries\":[]}");
   string stateHash=Hash(File.ReadAllBytes(Path.Combine(destination,"state.json")));var target=new Target{RegistryPath=registry,DisplayName="Test"};
   Apply(target,destination,false);Apply(target,destination,false);var doc=XDocument.Load(registry);
   byte[] registryBytes=File.ReadAllBytes(registry);if(registryBytes.Length>2&&registryBytes[0]==239&&registryBytes[1]==187&&registryBytes[2]==191)throw new Exception("Altium registry must use UTF-8 without a BOM.");
   if(Directory.GetFiles(destination,"*.ins",SearchOption.AllDirectories).Length!=1)throw new Exception("Backup registrations leaked into the live extension folder.");
   if(doc.Root.Elements("Item").Count(e=>(string)e.Attribute("HRID")==Hrid)!=1||doc.Root.Elements("Item").Count(e=>(string)e.Attribute("HRID")=="ExistingPlugin")!=1)throw new Exception("Registry preservation/idempotency failed.");
   var expected=Payload();foreach(string file in Files)if(Hash(File.ReadAllBytes(Path.Combine(destination,file)))!=Hash(expected[file]))throw new Exception("Payload copy failed.");
   if(Hash(File.ReadAllBytes(Path.Combine(destination,"state.json")))!=stateHash)throw new Exception("User settings changed.");
   File.WriteAllText(Path.Combine(destination,"state.json"),"{\"Enabled\":true,\"OwnedLibraries\":[\"C:\\\\example.SchLib\"]}");
   bool blocked=false;try{Apply(target,destination,true);}catch(InvalidOperationException){blocked=true;}if(!blocked)throw new Exception("Unsafe uninstall was not blocked.");
   File.WriteAllText(Path.Combine(destination,"state.json"),"{\"Enabled\":false,\"OwnedLibraries\":[]}");Apply(target,destination,true);doc=XDocument.Load(registry);
   if(doc.Root.Elements("Item").Any(e=>(string)e.Attribute("HRID")==Hrid)||doc.Root.Elements("Item").Count()!=1)throw new Exception("Uninstall preservation failed.");
   File.WriteAllText(Path.Combine(folder,"result.txt"),"PASS: payload integrity, install, idempotent upgrade, existing extension preservation, state preservation, owned-registration uninstall guard, clean unregister.");
  }
 }
 internal sealed class SetupForm:Form {
  readonly ComboBox target=new ComboBox();readonly TextBox status=new TextBox();readonly Button install=new Button(),remove=new Button();
  public SetupForm(){
   Text="Library Search Paths Setup";ClientSize=new Size(580,350);FormBorderStyle=FormBorderStyle.FixedDialog;MaximizeBox=false;StartPosition=FormStartPosition.CenterScreen;Font=new Font("Segoe UI",9F);
   Controls.Add(new Label{Text="Library Search Paths",Font=new Font("Segoe UI",17F,FontStyle.Bold),AutoSize=true,Left=20,Top=18});
   Controls.Add(new Label{Text="Adds schematic libraries from Altium's existing project search paths.",AutoSize=true,Left=22,Top=58});
   Controls.Add(new Label{Text="Altium installation",AutoSize=true,Left=22,Top=100});
   target.SetBounds(22,124,536,28);target.DropDownStyle=ComboBoxStyle.DropDownList;foreach(var item in Engine.Discover())target.Items.Add(item);if(target.Items.Count>0)target.SelectedIndex=0;Controls.Add(target);
   status.SetBounds(22,170,536,110);status.Multiline=true;status.ReadOnly=true;status.BorderStyle=BorderStyle.None;status.BackColor=SystemColors.Control;status.Text=target.Items.Count==0?"No supported Altium Designer 26 installation was found.":"Save and close Altium before installing.\r\n\r\nAfter installation, one checkbox in Altium enables discovery and the Search Paths tab.";Controls.Add(status);
   install.Text="Install / Update";install.SetBounds(22,300,145,30);install.Enabled=target.Items.Count>0;install.Click+=(s,e)=>Run(false);Controls.Add(install);
   remove.Text="Remove";remove.SetBounds(180,300,100,30);remove.Enabled=target.Items.Count>0;remove.Click+=(s,e)=>Run(true);Controls.Add(remove);
   var close=new Button{Text="Close"};close.SetBounds(458,300,100,30);close.Click+=(s,e)=>Close();Controls.Add(close);CancelButton=close;
  }
  void Run(bool uninstall){try{Engine.RequireClosed();status.Text=Engine.Apply((Target)target.SelectedItem,Engine.DefaultDestination,uninstall);}catch(UnauthorizedAccessException){status.Text="Windows denied write access. Close this installer, right-click it and choose Run as administrator.";}catch(Exception e){status.Text=e.Message;}}
 }
 internal static class Program {
  [STAThread] static int Main(string[] args){
   try {
    if(args.Length==2&&args[0]=="--self-test"){Engine.SelfTest(args[1]);return 0;}
    if(args.Length==3&&(args[0]=="--install"||args[0]=="--remove")){
     Engine.RequireClosed();var target=Engine.Discover().SingleOrDefault(t=>String.Equals(t.RegistryPath,args[1],StringComparison.OrdinalIgnoreCase));if(target==null)throw new InvalidOperationException("Unknown or unsupported Altium installation.");
     string message=Engine.Apply(target,Engine.DefaultDestination,args[0]=="--remove");File.WriteAllText(args[2],message);return 0;
    }
    Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);Application.Run(new SetupForm());return 0;
   }catch(Exception e){
    if(args.Length>=2){string path=args.Length==3?args[2]:Path.Combine(Path.GetFullPath(args[1]),"error.txt");try{Directory.CreateDirectory(Path.GetDirectoryName(path));File.WriteAllText(path,e.ToString());}catch{}}
    else MessageBox.Show(e.Message,"Library Search Paths Setup",MessageBoxButtons.OK,MessageBoxIcon.Error);
    return 1;
   }
  }
 }
}
