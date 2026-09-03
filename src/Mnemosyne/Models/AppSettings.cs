namespace Mnemosyne.Models;

public class AppSettings
{
    public string Theme { get; set; } = "Dark";
    public string Language { get; set; } = "zh-CN";
    public string FontFamily { get; set; } = "Consolas";
    public double FontSize { get; set; } = 13;
    public bool IndentUseTabs { get; set; }
    public int IndentWidth { get; set; } = 4;
    public int LargeFileThresholdMB { get; set; } = 50;
    public bool WordWrap { get; set; }
    public bool ShowWhitespace { get; set; }
}
