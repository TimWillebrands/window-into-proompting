using PartyTown.Services.SystemOne;

namespace BackendTest.Infrastructure;

/// <summary>System One for World tests: Life picks the option whose label contains <paramref name="wanted"/>, else rests; other questions get no answer.</summary>
public sealed class MenuJudge(Func<string?> wanted) : ISystemOneJudge
{
    public Task<SystemOneAnswers> AskAsync(string state, IReadOnlyDictionary<string, SystemOneQuestion> questions, CancellationToken ct)
    {
        var answers = new SystemOneAnswers();
        if (questions.TryGetValue("next", out var question) && question is ChoiceQuestion menu)
        {
            var text = wanted();
            var pick = text is null ? null : menu.Options.FirstOrDefault(o => o.Value?.Contains(text) == true).Key;
            answers.Choices["next"] = menu.Options.Keys.ToDictionary(k => k, k => k == (pick ?? "0") ? 1.0 : 0.0);
        }
        return Task.FromResult(answers);
    }
}
