using System.Windows;
using System.Windows.Controls;
using FFmpegStudio.Core;

namespace FFmpegStudio.Desktop;

/// <summary>Maps slider steps to raw FFmpeg tokens without writing range coercion back to saved settings.</summary>
public sealed class PresetSlider : Slider
{
    public static readonly DependencyProperty ChoicesProperty=DependencyProperty.Register(nameof(Choices),typeof(IReadOnlyList<string>),typeof(PresetSlider),
        new PropertyMetadata(Array.Empty<string>(),OnInputChanged));
    public static readonly DependencyProperty PresetProperty=DependencyProperty.Register(nameof(Preset),typeof(string),typeof(PresetSlider),
        new FrameworkPropertyMetadata("",FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,OnInputChanged));
    public static readonly DependencyProperty DefaultPresetProperty=DependencyProperty.Register(nameof(DefaultPreset),typeof(string),typeof(PresetSlider),
        new PropertyMetadata("",OnInputChanged));
    private bool synchronizing;
    public IReadOnlyList<string> Choices {get=>(IReadOnlyList<string>)GetValue(ChoicesProperty);set=>SetValue(ChoicesProperty,value);}
    public string Preset {get=>(string)GetValue(PresetProperty);set=>SetValue(PresetProperty,value);}
    public string DefaultPreset {get=>(string)GetValue(DefaultPresetProperty);set=>SetValue(DefaultPresetProperty,value);}
    public PresetSlider(){IsSnapToTickEnabled=true;IsMoveToPointEnabled=true;TickFrequency=SmallChange=LargeChange=1;}
    private static void OnInputChanged(DependencyObject sender,DependencyPropertyChangedEventArgs e)=>((PresetSlider)sender).Synchronize();
    private void Synchronize()
    {
        synchronizing=true;
        try
        {
            Minimum=0;Maximum=Math.Max(0,Choices.Count-1);
            var index=Choices.ToList().IndexOf(string.IsNullOrEmpty(Preset)?DefaultPreset:Preset);
            Value=Math.Max(0,index);
        }
        finally{synchronizing=false;}
    }
    protected override void OnValueChanged(double oldValue,double newValue)
    {
        base.OnValueChanged(oldValue,newValue);
        if(!synchronizing&&Choices.Count>0)
        {
            var index=Math.Clamp((int)Math.Round(newValue),0,Choices.Count-1);
            SetCurrentValue(PresetProperty,Choices[index]);
        }
    }
}
