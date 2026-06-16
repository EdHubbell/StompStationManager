using Sonulab.App.ViewModels;
using Sonulab.Core.Model;
using Xunit;

public class ParameterFieldViewModelTests
{
    static NodeSchema Schema(string json) { NodeRecord.TryParse(json, out var r); return NodeSchema.FromRecord(r!); }

    [Fact] public void Float_field_exposes_range_and_round_trips_json()
    {
        var s = Schema(@"root\app\amp\gain:{""desc"":""Gain"",""value"":0.0,""type"":""float"",""min"":-20.0,""max"":20.0,""def"":0.0,""unit"":""dB""}");
        var f = new ParameterFieldViewModel(s, "3.5");
        Assert.Equal("float", f.Kind);
        Assert.Equal(-20.0, f.Min);
        Assert.Equal(20.0, f.Max);
        Assert.Equal(3.5, f.Number);
        f.Number = -6.0;
        Assert.Equal("-6", f.ToJsonValue());
    }

    [Fact] public void Enum_field_exposes_options_and_quotes_value()
    {
        var s = Schema(@"root\app\reverb\mode:{""desc"":""Mode"",""value"":""ROOM"",""type"":""enum"",""options"":[""ROOM"",""HALL""]}");
        var f = new ParameterFieldViewModel(s, "\"ROOM\"");
        Assert.Equal("enum", f.Kind);
        Assert.Equal(new[] { "ROOM", "HALL" }, f.Options);
        f.Text = "HALL";
        Assert.Equal("\"HALL\"", f.ToJsonValue());
    }
}
