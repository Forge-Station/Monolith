namespace Content.Client._WF.Cockpit;

/// <summary>An MFD page whose selector only shows while the page has something to show.</summary>
public interface IWFCockpitOptionalPage
{
    bool Available { get; }
}
