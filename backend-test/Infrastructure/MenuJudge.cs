using PartyTown.Services.SystemOne;

namespace BackendTest.Infrastructure;

public sealed class MenuJudge(Func<string?> wanted, Func<string, double?>? nouls = null) : ISystemOneJudge
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
        foreach (var key in questions.Keys)
            if (nouls?.Invoke(key) is { } p) answers.Nouls[key] = p;
        return Task.FromResult(answers);
    }
}
