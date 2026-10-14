namespace AgenticLab.Web.Components.Pages;

/// <summary>Backend-free Development catalogue for shared controls and conversation presentation.</summary>
public partial class DesignSystem
{
    private int _activations;
    private bool _following;
    private bool _manual;
    private bool _checked;
    private int _delay = 500;
    private string _name = "Research agent";
    private string _replySample = """
        # Project summary

        **Ready for review**, with *two changes* and `inline code`.

        ## Findings

        - Format the current reply.
        - Preserve earlier turns.
          - Keep user text unchanged.

        1. Review the changes.
        2. Run the tests.

        > Replies are untrusted content.

        ```csharp
        var summary = "A deliberately long code line stays within its own scrolling region rather than widening the conversation or covering neighboring content on a narrow mobile screen.";
        ```

        | Area | Status | Evidence |
        | --- | --- | --- |
        | Formatting | Ready | Headings, lists and code |
        | Security | Ready | Literal HTML and restricted links |

        [Details](https://example.com/report?view=full&mode=review), [HTTP](http://example.com), [Email](mailto:review@example.com).

        [Blocked script](javascript:alert%281%29), [Blocked data](data:text/html,test).

        ![Image description](https://images.invalid/markdown-sentinel.png)

        <img src="https://images.invalid/raw-sentinel.png" onerror="window.markdownExecuted=true">

        <script>window.markdownExecuted=true</script>
        """;
    private static readonly (string Name, string Token)[] Colours =
    [
        ("Surface", "--lab-surface"), ("Muted surface", "--lab-surface-muted"),
        ("Ink", "--lab-ink"), ("Accent", "--lab-accent"),
        ("User", "--lab-user"), ("Host", "--lab-host"),
        ("Model", "--lab-model"), ("Tool", "--lab-tool"), ("Danger", "--lab-danger")
    ];

    /// <inheritdoc />
    protected override void OnInitialized()
    {
        if (!Environment.IsDevelopment()) Navigation.NotFound();
    }

    private void CountActivation() => _activations++;
}