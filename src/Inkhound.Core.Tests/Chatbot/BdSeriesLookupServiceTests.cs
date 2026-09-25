using Foundation.Core.Chatbot;
using Inkhound.Core.Chatbot;
using Inkhound.Core.Chatbot.Services;
using Inkhound.Core.Models;
using Inkhound.Core.Sources;

namespace Inkhound.Core.Tests.Chatbot;

public class BdSeriesLookupServiceTests
{
    // Passerelle factice : le pipeline de recherche est entièrement testable sans base ni réseau.
    private sealed class FakeGateway(
        IReadOnlyList<SourceVolume> volumes,
        Dictionary<string, IReadOnlyList<SourceIssue>>? issuesBySourceId = null,
        Exception? searchError = null) : IInkhoundChatbotGateway
    {
        public int IssueCallCount { get; private set; }

        public Task<Page<SourceVolume>> SearchVolumesAsync(string name, int page = 1, int? pageSize = null, CancellationToken ct = default) =>
            searchError is not null
                ? Task.FromException<Page<SourceVolume>>(searchError)
                : Task.FromResult(new Page<SourceVolume> { Items = volumes, PageNumber = 1, PageSize = 16, TotalItems = volumes.Count });

        public Task<Page<SourceIssue>> GetIssuesBySourceAsync(string source, string sourceVolumeId, int page = 1, int? pageSize = null, CancellationToken ct = default)
        {
            IssueCallCount++;
            var items = issuesBySourceId?.GetValueOrDefault(sourceVolumeId) ?? [];
            return Task.FromResult(new Page<SourceIssue> { Items = items, PageNumber = 1, PageSize = 500, TotalItems = items.Count });
        }

        public Task<IReadOnlyList<Library>> GetLibrariesAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<Library>>([]);

        public Task<IReadOnlyList<Volume>> GetLibraryVolumesAsync(Guid libraryId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<Volume>>([]);

        public Task<Volume> AddVolumeToLibraryAsync(Guid libraryId, string source, string sourceId, CancellationToken ct = default) =>
            Task.FromResult(new Volume());

        public Task SetVolumeAgeRatingAsync(Guid volumeId, AgeRating ageRating, CancellationToken ct = default) =>
            Task.CompletedTask;
    }

    private sealed class NullTrace : IChatTrace
    {
        public void Debug(string message) { }
        public void Info(string message) { }
        public void Warn(string message) { }
        public void Error(string message, Exception? ex = null) { }
    }

    private static SourceVolume Volume(string sourceId, string name, int countOfIssues = 10) =>
        new(sourceId, "bedetheque", name, 2020, countOfIssues, "Éditeur", "Description", null, null);

    private static SourceIssue Issue(string sourceId, string? name, string number) =>
        new(sourceId, "bedetheque", name, number, null, null, null, null);

    private static BdSeriesLookupService Service(FakeGateway gateway) => new(gateway, new NullTrace());

    [Fact]
    public async Task No_result_yields_NotFound()
    {
        var outcome = await Service(new FakeGateway([])).SearchAsync("inconnue", null, null, CancellationToken.None);

        var notFound = Assert.IsType<BdLookupOutcome.NotFound>(outcome);
        Assert.Contains("inconnue", notFound.Message);
    }

    [Fact]
    public async Task A_single_result_is_resolved_directly()
    {
        var gateway = new FakeGateway(
            [Volume("1", "Lanfeust de Troy")],
            new() { ["1"] = [Issue("i1", "L'ivoire du Magohamoth", "1")] });

        var outcome = await Service(gateway).SearchAsync("Lanfeust", null, null, CancellationToken.None);

        var resolved = Assert.IsType<BdLookupOutcome.Resolved>(outcome);
        Assert.Equal("Lanfeust de Troy", resolved.Detail.Series.Name);
        Assert.Single(resolved.Detail.Issues);
    }

    [Fact]
    public async Task Several_results_without_filter_are_ambiguous()
    {
        var gateway = new FakeGateway([Volume("1", "Lanfeust de Troy"), Volume("2", "Lanfeust des Étoiles")]);

        var outcome = await Service(gateway).SearchAsync("Lanfeust", null, null, CancellationToken.None);

        var ambiguous = Assert.IsType<BdLookupOutcome.Ambiguous>(outcome);
        Assert.Equal(2, ambiguous.Candidates.Count);
        // Aucun détail n'est résolu tant que l'utilisateur n'a pas tranché.
        Assert.Equal(0, gateway.IssueCallCount);
    }

    [Fact]
    public async Task A_number_filter_discards_series_that_are_too_short()
    {
        // Le tome 8 ne peut pas appartenir à une série de 3 tomes.
        var gateway = new FakeGateway(
            [Volume("short", "Série courte", countOfIssues: 3), Volume("long", "Série longue", countOfIssues: 12)],
            new() { ["long"] = [Issue("i1", "Tome 8", "8")] });

        var outcome = await Service(gateway).SearchAsync("Série", album: null, numeroAlbum: "8", CancellationToken.None);

        var resolved = Assert.IsType<BdLookupOutcome.Resolved>(outcome);
        Assert.Equal("Série longue", resolved.Detail.Series.Name);
    }

    [Fact]
    public async Task A_non_numeric_number_is_treated_as_no_constraint()
    {
        // « HS » ou « 47Ter » ne sont pas comparables à un nombre de tomes : on ne doit écarter personne.
        var gateway = new FakeGateway(
            [Volume("short", "Série courte", countOfIssues: 1), Volume("long", "Série longue", countOfIssues: 12)],
            new() { ["short"] = [], ["long"] = [] });

        var outcome = await Service(gateway).SearchAsync("Série", album: null, numeroAlbum: "HS1", CancellationToken.None);

        var resolved = Assert.IsType<BdLookupOutcome.Resolved>(outcome);
        Assert.Equal("Série courte", resolved.Detail.Series.Name);
    }

    [Fact]
    public async Task An_album_filter_matches_in_both_directions()
    {
        // La couverture porte une mention devant le titre réel : le titre de la source est contenu
        // dans le titre OCR, sans que l'inverse soit vrai.
        var gateway = new FakeGateway(
            [Volume("a", "Série A"), Volume("b", "Série B")],
            new()
            {
                ["a"] = [Issue("i1", "Un tout autre titre", "1")],
                ["b"] = [Issue("i2", "Le Temps des bricoleurs", "1")],
            });

        var outcome = await Service(gateway).SearchAsync(
            "Série", album: "Première époque - Le Temps des bricoleurs", numeroAlbum: null, CancellationToken.None);

        var resolved = Assert.IsType<BdLookupOutcome.Resolved>(outcome);
        Assert.Equal("Série B", resolved.Detail.Series.Name);
    }

    [Fact]
    public async Task No_candidate_matching_the_filters_falls_back_to_the_first_result()
    {
        // On ne clôt pas la demande : l'utilisateur pourra changer de correspondance ensuite.
        var gateway = new FakeGateway(
            [Volume("a", "Série A"), Volume("b", "Série B")],
            new() { ["a"] = [Issue("i1", "Titre A", "1")], ["b"] = [Issue("i2", "Titre B", "1")] });

        var outcome = await Service(gateway).SearchAsync(
            "Série", album: "Titre totalement absent", numeroAlbum: null, CancellationToken.None);

        var resolved = Assert.IsType<BdLookupOutcome.Resolved>(outcome);
        Assert.Equal("Série A", resolved.Detail.Series.Name);
        Assert.Equal(2, resolved.Candidates.Count);
    }

    [Fact]
    public async Task A_gateway_failure_becomes_a_Failed_outcome()
    {
        var gateway = new FakeGateway([], searchError: new ChatbotGatewayException("source indisponible"));

        var outcome = await Service(gateway).SearchAsync("Lanfeust", null, null, CancellationToken.None);

        var failed = Assert.IsType<BdLookupOutcome.Failed>(outcome);
        Assert.Contains("source indisponible", failed.Message);
    }
}
