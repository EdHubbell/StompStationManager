using Sonulab.Core.Model;
using Xunit;

public class NodeRecordTests
{
    [Fact] public void TryParse_splits_path_and_json()
    {
        Assert.True(NodeRecord.TryParse(@"root\app\amp\amp:{""value"":""Pano-Verb"",""type"":""plist""}", out var r));
        Assert.Equal(@"root\app\amp\amp", r.Path);
        Assert.Equal("Pano-Verb", r.ValueString);
    }

    [Fact] public void TryParse_reads_numeric_value()
    {
        Assert.True(NodeRecord.TryParse(@"root\app\gate\threshold:{""value"":-60.5}", out var r));
        Assert.Equal(-60.5, r.ValueNumber);
        Assert.Null(r.ValueString);
    }

    [Fact] public void TryParse_returns_false_for_garbage()
    {
        Assert.False(NodeRecord.TryParse("not a record", out _));
    }
}
