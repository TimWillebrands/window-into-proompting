namespace PartyTown.Data.Entities;

public sealed class PersonaMemory
{
    public Guid Id { get; set; }
    public Guid PersonaId { get; set; }
    public Guid PartyId { get; set; }
    public Guid ChatGroupId { get; set; }
    public int SourceMessageId { get; set; }
    public string Text { get; set; } = "";
    public DateTimeOffset EncodedAt { get; set; }
}
