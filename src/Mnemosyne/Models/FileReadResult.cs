using System.Text;

namespace Mnemosyne.Models;

public sealed record FileReadResult(string Text, Encoding Encoding, LineEnding LineEnding);
