namespace ElectronicsAI.Design;

static class PatternChecks
{
    public static ClauseCheck Check(
        string name,
        string pass,
        string fail,
        Clause body,
        PartQuery? subject = null,
        string? absent = null,
        PartQuery? only = null,
        PartQuery? unless = null,
        Func<SketchBoard, string>? failOf = null)
    {
        return new ClauseCheck
        {
            Name = name,
            Pass = pass,
            Fail = fail,
            Body = body,
            Subject = subject,
            Absent = absent,
            Only = only,
            Unless = unless,
            FailOf = failOf,
        };
    }
}
