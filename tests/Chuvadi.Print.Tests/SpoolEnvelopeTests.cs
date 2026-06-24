using Chuvadi.Print;

namespace Chuvadi.Print.Tests;

public class SpoolEnvelopeTests
{
    [Fact]
    public void Empty_pdf_round_trips()
    {
        var env = new SpoolEnvelope(System.Array.Empty<byte>(), new PrintSettings());
        var back = SpoolEnvelope.FromArray(env.ToArray());
        Assert.Empty(back.PdfBytes);
        Assert.Equal(1, back.Settings.Copies);
    }

    [Fact]
    public void Truncated_envelope_is_rejected()
    {
        var env = new SpoolEnvelope(new byte[] { 1, 2, 3, 4 }, new PrintSettings());
        byte[] data = env.ToArray();
        byte[] truncated = data[..(data.Length - 3)];
        Assert.Throws<System.IO.InvalidDataException>(() => SpoolEnvelope.FromArray(truncated));
    }

    [Fact]
    public void Null_arguments_are_rejected()
    {
        Assert.Throws<ArgumentNullException>(() => new SpoolEnvelope(null!, new PrintSettings()));
        Assert.Throws<ArgumentNullException>(() => new SpoolEnvelope(System.Array.Empty<byte>(), null!));
    }
}
