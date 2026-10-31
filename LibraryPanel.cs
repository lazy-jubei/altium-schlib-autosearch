using System;
using System.Drawing;
using System.Windows.Forms;
using DXP;
using Point=System.Drawing.Point;

namespace SchLibSearchPaths {
 public sealed class LibraryPanelView:ServerPanelView {
  public LibraryPanelView(LibraryPanel form):base(form,LibraryModule.Name,"Library Search Paths"){}
 }
 public sealed class LibraryPanel:ServerPanelForm {
  readonly CheckBox enabled;
  readonly Label status;
  readonly Timer timer;
  bool updating;
  public LibraryPanel() {
   Text="Library Search Paths";ClientSize=new Size(340,240);FormBorderStyle=FormBorderStyle.None;AutoScaleMode=AutoScaleMode.Dpi;
   BackColor=Color.FromArgb(45,45,48);ForeColor=Color.Gainsboro;Font=new Font("Segoe UI",9F);
   var layout=new TableLayoutPanel {Dock=DockStyle.Top,AutoSize=true,ColumnCount=1,Padding=new Padding(12)};
   layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
   enabled=new LightCheckBox {Text="Enable library search paths",Dock=DockStyle.Top,Height=30,Checked=Service.Enabled};
   var description=new Label {Text="Use the folders in Project Options → Search Paths.",AutoSize=true,MaximumSize=new Size(360,0),Margin=new Padding(3,8,3,10)};
   status=new Label {AutoSize=true,MaximumSize=new Size(360,0),ForeColor=Color.Silver,Margin=new Padding(3,8,3,8)};
   var paths=new LinkLabel {Text="Project search paths…",AutoSize=true,LinkColor=Color.LightSkyBlue,ActiveLinkColor=Color.White,VisitedLinkColor=Color.LightSkyBlue,Margin=new Padding(3,8,3,4)};
   paths.LinkClicked+=(s,e)=>{try{Service.EnsureSearchPathsOption();DXP.Utils.RunCommand("WorkspaceManager:DocumentOptions","ObjectKind=FocusedProject|Action=SearchPaths");}catch(Exception ex){status.Text=ex.Message;}};
   enabled.CheckedChanged+=(s,e)=>{
    if(updating)return;
    try{if(enabled.Checked){Service.Enable();Service.Sync(true);}else Service.Disable();}
    catch(Exception ex){MessageBox.Show(ex.Message,"Library Search Paths");}
    RefreshStatus();
   };
   var tip=new ToolTip();tip.SetToolTip(enabled,"Also enables Altium's Search Paths tab. Turning this off removes only automatically registered libraries; the tab stays available.");
   layout.Controls.Add(enabled);layout.Controls.Add(description);layout.Controls.Add(paths);layout.Controls.Add(status);Controls.Add(layout);
   timer=new Timer {Interval=1000};timer.Tick+=(s,e)=>{if(Visible)RefreshStatus();};timer.Start();
   RefreshStatus();
  }
  void RefreshStatus(){updating=true;try{enabled.Checked=Service.Enabled;status.Text=Service.Enabled?Service.Summary:"Automatic discovery is off.";}finally{updating=false;}}
  protected override void Dispose(bool disposing){if(disposing)timer?.Dispose();base.Dispose(disposing);}
 }
 internal sealed class LightCheckBox:CheckBox {
  public LightCheckBox(){SetStyle(ControlStyles.UserPaint,true);}
  protected override void OnPaint(PaintEventArgs e) {
   e.Graphics.Clear(Parent?.BackColor??BackColor);
   var box=new Rectangle(1,(Height-16)/2,16,16);
   using(var brush=new SolidBrush(Checked?Color.FromArgb(44,126,180):Color.FromArgb(68,68,68)))e.Graphics.FillRectangle(brush,box);
   using(var pen=new Pen(Color.Silver))e.Graphics.DrawRectangle(pen,box);
   if(Checked)using(var pen=new Pen(Color.White,2))e.Graphics.DrawLines(pen,new[]{new Point(box.Left+3,box.Top+8),new Point(box.Left+7,box.Top+12),new Point(box.Left+13,box.Top+4)});
   TextRenderer.DrawText(e.Graphics,Text,Font,new Rectangle(25,0,Width-25,Height),Color.Gainsboro,TextFormatFlags.Left|TextFormatFlags.VerticalCenter);
   if(Focused)ControlPaint.DrawFocusRectangle(e.Graphics,new Rectangle(23,3,Math.Max(1,Width-26),Height-6),Color.White,BackColor);
  }
 }
}
