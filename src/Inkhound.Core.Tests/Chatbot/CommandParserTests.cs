using System.Text.Json;
using Foundation.Core.Chatbot.Features;
using Foundation.Core.Chatbot.Matrix.Models;

namespace Inkhound.Core.Tests.Chatbot;

public class CommandParserTests
{
    private static RoomEvent Event(string type, object content) => new()
    {
        EventId = "$evt:server",
        Sender = "@user:server",
        Type = type,
        OriginServerTs = 0,
        Content = JsonSerializer.SerializeToElement(content),
    };

    [Fact]
    public void TryParse_splits_name_and_args()
    {
        var command = CommandParser.TryParse(Event("m.room.message", new { msgtype = "m.text", body = "!bd-search Astérix" }));

        Assert.NotNull(command);
        Assert.Equal("bd-search", command!.Name);
        Assert.Equal(["Astérix"], command.Args);
    }

    [Fact]
    public void TryParse_keeps_every_argument_token()
    {
        var command = CommandParser.TryParse(Event("m.room.message", new { msgtype = "m.text", body = "!bd-search Le  Temps   des bricoleurs" }));

        // Les séparateurs vides sont supprimés, mais aucun token n'est perdu : la commande recolle
        // ensuite les arguments pour former le titre recherché.
        Assert.Equal(["Le", "Temps", "des", "bricoleurs"], command!.Args);
    }

    [Fact]
    public void TryParse_accepts_an_image_caption()
    {
        // Un événement m.image porte la commande dans sa légende : c'est ce qui permet d'envoyer
        // une couverture avec « !bd-scan » en une seule action.
        var command = CommandParser.TryParse(Event("m.room.message", new { msgtype = "m.image", body = "!bd-scan" }));

        Assert.NotNull(command);
        Assert.Equal("bd-scan", command!.Name);
        Assert.Empty(command.Args);
    }

    [Theory]
    [InlineData("m.text", "bonjour")]       // pas de préfixe !
    [InlineData("m.text", "")]
    [InlineData("m.text", "!")]             // préfixe seul, aucun nom de commande
    [InlineData("m.notice", "!ping")]       // msgtype non pris en charge
    public void TryParse_returns_null_for_non_commands(string msgType, string body)
    {
        Assert.Null(CommandParser.TryParse(Event("m.room.message", new { msgtype = msgType, body })));
    }

    [Fact]
    public void TryParse_ignores_non_message_events()
    {
        Assert.Null(CommandParser.TryParse(Event("m.room.encrypted", new { msgtype = "m.text", body = "!ping" })));
    }

    [Fact]
    public void TryParse_exposes_the_replied_to_event_id()
    {
        // JSON écrit à la main : les clés Matrix contiennent des points, impossibles en nom de
        // propriété d'un type anonyme C#.
        var json = JsonDocument.Parse("""
            {"msgtype":"m.text","body":"!image-info",
             "m.relates_to":{"m.in_reply_to":{"event_id":"$image:server"}}}
            """).RootElement.Clone();

        var roomEvent = new RoomEvent
        {
            EventId = "$evt:server",
            Sender = "@user:server",
            Type = "m.room.message",
            Content = json,
        };

        Assert.Equal("$image:server", CommandParser.TryParse(roomEvent)!.ReplyToEventId);
    }
}
