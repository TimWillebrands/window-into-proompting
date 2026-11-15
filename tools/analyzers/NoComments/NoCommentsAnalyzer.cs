using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace NoComments;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class NoCommentsAnalyzer : DiagnosticAnalyzer
{
    public const string Id = "PT0001";

    private static readonly DiagnosticDescriptor Rule = new(
        Id,
        "No comments",
        "Comments are not allowed: say what the code does in names, types and tests; put why in the commit message or the issue",
        "Style",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Comments go stale, nobody reads them, and they excuse workarounds instead of fixing them. #pragma is a directive and stays; the reason after it doesn't.");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxTreeAction(Analyze);
    }

    private static void Analyze(SyntaxTreeAnalysisContext context)
    {
        var root = context.Tree.GetRoot(context.CancellationToken);
        foreach (var trivia in root.DescendantTrivia(descendIntoTrivia: true))
        {
            if (!IsComment(trivia.Kind()))
                continue;
            context.ReportDiagnostic(Diagnostic.Create(Rule, Location.Create(context.Tree, trivia.FullSpan)));
        }
    }

    private static bool IsComment(SyntaxKind kind) => kind is
        SyntaxKind.SingleLineCommentTrivia or
        SyntaxKind.MultiLineCommentTrivia or
        SyntaxKind.SingleLineDocumentationCommentTrivia or
        SyntaxKind.MultiLineDocumentationCommentTrivia;
}
