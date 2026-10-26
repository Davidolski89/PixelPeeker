using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using FFmpegStudio.Core;

namespace FFmpegStudio.Desktop;

public sealed class CutEditorControl : UserControl
{
    public CutEditorControl()
    {
        SetResourceReference(ForegroundProperty,"Text");
        var panel=new StackPanel{Margin=new Thickness(10)};
        Text("Video schneiden",18);
        var enabled=new CheckBox{Name="CutsEnabled",Margin=new Thickness(0,4,0,6)};
        enabled.SetResourceReference(ContentControl.ContentProperty,L.ResourceKey("Schnittbereiche beim Export entfernen"));
        enabled.SetBinding(System.Windows.Controls.Primitives.ToggleButton.IsCheckedProperty,new Binding("SelectedMedia.Options.CutsEnabled"){Mode=BindingMode.TwoWay});panel.Children.Add(enabled);
        var markers=new WrapPanel();panel.Children.Add(markers);
        Button(markers,"MarkCutStart","Start setzen");Button(markers,"MarkCutEnd","Ende setzen + entfernen");Button(markers,"CutToEnd","Bis Dateiende entfernen");
        var actions=new WrapPanel{Margin=new Thickness(0,4,0,0)};panel.Children.Add(actions);
        Button(actions,"JumpCutStart","Zum Start");Button(actions,"JumpCutEnd","Zum Ende");Button(actions,"RemoveCut","Bereich behalten");Button(actions,"ClearCuts","Alle Bereiche behalten");
        Bound("CutPositionStatus");Bound("CutSummary");
        var list=new ListBox{Name="CutList",MinHeight=85,MaxHeight=190,DisplayMemberPath="Label",Margin=new Thickness(0,10,0,8)};
        list.SetBinding(ItemsControl.ItemsSourceProperty,new Binding("CutEntries"));
        list.SetBinding(System.Windows.Controls.Primitives.Selector.SelectedItemProperty,new Binding("SelectedCut"){Mode=BindingMode.TwoWay});panel.Children.Add(list);
        Text("1. Im Vorschaubild das erste zu entfernende Bild suchen und Start setzen.\n2. Das erste danach zu erhaltende Bild suchen und Ende setzen.\n3. Für weitere Bereiche wiederholen; anschließend zur Queue hinzufügen.");
        Text("Überlappende Bereiche werden zusammengeführt. Das Endbild bleibt erhalten. Die Vorschau zeigt weiterhin die Quellzeitachse; rote Abschnitte werden entfernt.");
        Text("Erhaltener Ton wird für exakte Schnittstellen neu codiert. Textuntertitel folgen dem Schnitt; Bilduntertitel bitte entfernen. Kapitel werden beim Schnitt weggelassen. Die Originaldatei bleibt erhalten.");
        Content=new ScrollViewer{Content=panel,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};
        void Text(string source,double size=12){var text=new TextBlock{TextWrapping=TextWrapping.Wrap,FontSize=size,Margin=size>12?new Thickness(0,0,0,6):new Thickness(0,5,0,8)};text.SetResourceReference(TextBlock.TextProperty,L.ResourceKey(source));panel.Children.Add(text);}
        void Bound(string path){var text=new TextBlock{TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,6,0,2)};text.SetBinding(TextBlock.TextProperty,new Binding(path));panel.Children.Add(text);}
        static void Button(Panel parent,string key,string text)
        {
            var button=new Button{Name=key,Padding=new Thickness(8,5,8,5)};
            button.SetResourceReference(ContentControl.ContentProperty,L.ResourceKey(text));ButtonHints.SetKey(button,key);
            button.SetBinding(System.Windows.Controls.Primitives.ButtonBase.CommandProperty,new Binding(key+"Command"));parent.Children.Add(button);
        }
    }
}
