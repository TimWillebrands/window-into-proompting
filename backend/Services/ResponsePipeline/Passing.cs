using System.Text;
using System.Text.RegularExpressions;

namespace PartyTown.Services.ResponsePipeline;

public static partial class Passing
{
    public const string Mark = "(pass)";

    public const string Cue = "If, being who you are right now, you'd let this one go by after all, write only: (pass)";

    public const string Reason = "Let this one go by.";

    public static bool Forbidden(bool owed, bool direct, bool named, bool fromHuman)
        => owed || fromHuman && (direct || named);

    public static bool Allowed(bool owed, bool direct, bool named, bool fromHuman, bool torn, bool windingDown)
        => !Forbidden(owed, direct, named, fromHuman) && (!direct && !named || torn || windingDown);

    [GeneratedRegex(@"\*[^*\n]*\*")]
    private static partial Regex StageDirection();

    [GeneratedRegex(@"^[\s""'“”‘’`*_~.,!?…:;-]*[\(\[\{<]\s*pass\s*[\)\]\}>][\s""'“”‘’`*_~.,!?…:;-]*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex WholeMark();

    [GeneratedRegex(@"^[\s""'“”‘’`*_~.,!?…:;-]*(?:[\(\[\{<]\s*(?:p(?:a(?:s(?:s\s*(?:[\)\]\}>][\s""'“”‘’`*_~.,!?…:;-]*)?)?)?)?)?)?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex MarkStart();

    [GeneratedRegex(@"\s*\(\s*pass\s*\)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Token();

    [GeneratedRegex(@"\((?:\s*(?:p(?:a(?:s(?:s\s*)?)?)?)?)?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex TokenStart();

    private static string WithoutStageDirections(string text) => StageDirection().Replace(text, " ");

    public static bool IsMark(string? text)
        => !string.IsNullOrWhiteSpace(text) && (WholeMark().IsMatch(text) || WholeMark().IsMatch(WithoutStageDirections(text)));

    public static bool MayBe(string? prefix)
    {
        if (string.IsNullOrEmpty(prefix) || MarkStart().IsMatch(prefix)) return true;
        var rest = WithoutStageDirections(prefix);
        var open = rest.LastIndexOf('*');
        if (open >= 0) rest = rest[..open];
        return MarkStart().IsMatch(rest);
    }

    public static string StripToken(string text) => Token().Replace(text, string.Empty);

    public sealed class Gate(Func<string, Task> release)
    {
        private readonly StringBuilder _held = new();
        private bool _open;

        public string Held => _held.ToString();

        public async Task ChunkAsync(string chunk)
        {
            _held.Append(chunk);
            if (!_open)
            {
                if (MayBe(_held.ToString())) return;
                _open = true;
            }
            await FlushAsync(final: false);
        }

        public async Task EndAsync()
        {
            if (_held.Length == 0) return;
            if (!_open && IsMark(_held.ToString()))
            {
                _held.Clear();
                return;
            }
            _open = true;
            await FlushAsync(final: true);
        }

        private async Task FlushAsync(bool final)
        {
            var text = StripToken(_held.ToString());
            var keep = final ? 0 : TokenStart().Match(text) is { Success: true } m ? m.Length : 0;
            var out1 = text[..^keep];
            _held.Clear().Append(text[^keep..]);
            if (out1.Length > 0) await release(out1);
        }

        public void Reset()
        {
            _held.Clear();
            _open = false;
        }
    }
}
