using Inkhound.Core.Chatbot;

namespace Inkhound.Core.Tests.Chatbot;

/// <summary>
/// Contrat d'erreur de la passerelle : les commandes du chatbot n'interceptent que
/// <see cref="ChatbotGatewayException"/>, donc toute erreur du domaine doit être convertie — sauf
/// l'annulation, qui doit remonter telle quelle pour que l'arrêt du bot interrompe la commande.
/// </summary>
public class InkhoundChatbotGatewayTests
{
    [Fact]
    public async Task Domain_exceptions_are_wrapped()
    {
        var ex = await Assert.ThrowsAsync<ChatbotGatewayException>(() =>
            InkhoundChatbotGateway.GuardAsync<int>(
                () => throw new InvalidOperationException("Unknown source 'foo'"), "la recherche"));

        Assert.Contains("la recherche", ex.Message);
        Assert.Contains("Unknown source 'foo'", ex.Message);
        Assert.IsType<InvalidOperationException>(ex.InnerException);
    }

    [Fact]
    public async Task A_non_numeric_source_id_surfaces_as_a_gateway_error()
    {
        // AddVolumeFromSourceAsync fait un int.Parse(sourceId) : sans enveloppe, une FormatException
        // brute traverserait les commandes et ferait échouer le bot au lieu d'un message lisible.
        await Assert.ThrowsAsync<ChatbotGatewayException>(() =>
            InkhoundChatbotGateway.GuardAsync<int>(
                () => throw new FormatException("bad id"), "l'ajout de la série"));
    }

    [Fact]
    public async Task Cancellation_is_never_swallowed()
    {
        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            InkhoundChatbotGateway.GuardAsync<int>(
                () => throw new OperationCanceledException(), "la recherche"));
    }

    [Fact]
    public async Task An_already_wrapped_error_is_not_wrapped_twice()
    {
        var ex = await Assert.ThrowsAsync<ChatbotGatewayException>(() =>
            InkhoundChatbotGateway.GuardAsync<int>(
                () => throw new ChatbotGatewayException("message d'origine"), "la recherche"));

        Assert.Equal("message d'origine", ex.Message);
    }

    [Fact]
    public async Task A_successful_call_passes_its_result_through()
    {
        Assert.Equal(42, await InkhoundChatbotGateway.GuardAsync(() => Task.FromResult(42), "la lecture"));
    }
}
