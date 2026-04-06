using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

var winmdPath = args.Length > 0 ? args[0] : @"C:\Windows\System32\WinMetadata\Windows.Gaming.winmd";
var search    = args.Length > 1 ? args[1] : "LegacyGipGameControllerProvider";
Console.WriteLine($"Inspecting: {winmdPath}  search='{search}'\n");

using var stream = File.OpenRead(winmdPath);
using var pe = new PEReader(stream);
var mr = pe.GetMetadataReader();

// Build TypeRef lookup: token → "Namespace.Name"
var typeRefNames = new Dictionary<int, string>();
foreach (var trh in mr.TypeReferences)
{
    var tr = mr.GetTypeReference(trh);
    var name = mr.GetString(tr.Name);
    var ns   = mr.GetString(tr.Namespace);
    int token = MetadataTokens.GetToken(trh);
    typeRefNames[token] = string.IsNullOrEmpty(ns) ? name : $"{ns}.{name}";
}

var typeDefNames = new Dictionary<int, string>();
foreach (var tdh in mr.TypeDefinitions)
{
    var td = mr.GetTypeDefinition(tdh);
    var name = mr.GetString(td.Name);
    var ns   = mr.GetString(td.Namespace);
    int token = MetadataTokens.GetToken(tdh);
    typeDefNames[token] = string.IsNullOrEmpty(ns) ? name : $"{ns}.{name}";
}

// Signature decoder (no unsafe pointers)
int ReadCompressed(byte[] b, ref int pos)
{
    byte first = b[pos];
    if ((first & 0x80) == 0) { pos++; return first; }
    if ((first & 0xC0) == 0x80) { int v = ((first & 0x3F) << 8) | b[pos + 1]; pos += 2; return v; }
    int vv = ((first & 0x1F) << 24) | (b[pos + 1] << 16) | (b[pos + 2] << 8) | b[pos + 3]; pos += 4; return vv;
}

string ResolveRef(byte[] b, ref int pos)
{
    int c = ReadCompressed(b, ref pos);
    int table = c & 3; int row = c >> 2;
    if (table == 0) { int tok = MetadataTokens.GetToken(MetadataTokens.TypeDefinitionHandle(row)); return typeDefNames.GetValueOrDefault(tok, $"TD#{row}"); }
    if (table == 1) { int tok = MetadataTokens.GetToken(MetadataTokens.TypeReferenceHandle(row));  return typeRefNames.GetValueOrDefault(tok, $"TR#{row}"); }
    return $"TS#{row}";
}

string DecodeTypeAt(byte[] b, ref int pos)
{
    if (pos >= b.Length) return "?";
    byte et = b[pos++];
    switch (et)
    {
        case 0x01: return "void";
        case 0x02: return "bool";
        case 0x03: return "char";
        case 0x04: return "sbyte";
        case 0x05: return "byte";
        case 0x06: return "short";
        case 0x07: return "ushort";
        case 0x08: return "int";
        case 0x09: return "uint";
        case 0x0A: return "long";
        case 0x0B: return "ulong";
        case 0x0C: return "float";
        case 0x0D: return "double";
        case 0x0E: return "string";
        case 0x1C: return "object";
        case 0x11: case 0x12: return ResolveRef(b, ref pos);
        case 0x15: // GENERICINST
        {
            pos++; // class/value
            string baseName = ResolveRef(b, ref pos);
            int cnt = ReadCompressed(b, ref pos);
            var gargs = new List<string>();
            for (int i = 0; i < cnt; i++) gargs.Add(DecodeTypeAt(b, ref pos));
            return $"{baseName}<{string.Join(", ", gargs)}>";
        }
        case 0x1D: return "[]" + DecodeTypeAt(b, ref pos);
        default:   return $"et0x{et:X2}";
    }
}

string DecodeSig(byte[] sigBytes)
{
    int pos = 0;
    pos++; // calling convention
    int paramCount = ReadCompressed(sigBytes, ref pos);
    string retType = DecodeTypeAt(sigBytes, ref pos);
    var parms = new List<string>();
    for (int i = 0; i < paramCount; i++) parms.Add(DecodeTypeAt(sigBytes, ref pos));
    return $"{retType}({string.Join(", ", parms)})";
}

// Main search loop
foreach (var tdh in mr.TypeDefinitions)
{
    var td   = mr.GetTypeDefinition(tdh);
    var name = mr.GetString(td.Name);
    var ns   = mr.GetString(td.Namespace);
    var full = $"{ns}.{name}";
    if (!full.Contains(search, StringComparison.OrdinalIgnoreCase)) continue;

    Console.WriteLine($"=== {full} ===");

    foreach (var cah in td.GetCustomAttributes())
    {
        var ca = mr.GetCustomAttribute(cah);
        if (ca.Constructor.Kind != HandleKind.MemberReference) continue;
        var ctor2 = mr.GetMemberReference((MemberReferenceHandle)ca.Constructor);
        if (ctor2.Parent.Kind != HandleKind.TypeReference) continue;
        var tr2 = mr.GetTypeReference((TypeReferenceHandle)ctor2.Parent);
        if (!mr.GetString(tr2.Name).Contains("Guid")) continue;
        var blob = mr.GetBlobBytes(ca.Value);
        if (blob.Length >= 18) Console.WriteLine($"  [Guid] {new Guid(blob[2..18])}");
    }

    // Enum fields
    foreach (var fh in td.GetFields())
    {
        var f = mr.GetFieldDefinition(fh);
        var fname = mr.GetString(f.Name);
        if (fname == "value__") continue;
        var cv = f.GetDefaultValue();
        if (!cv.IsNil)
        {
            var blob = mr.GetBlobBytes(mr.GetConstant(cv).Value);
            int val = blob.Length == 4 ? BitConverter.ToInt32(blob) :
                      blob.Length == 2 ? BitConverter.ToInt16(blob) :
                      blob.Length == 1 ? blob[0] : -1;
            Console.WriteLine($"  {fname} = {val} (0x{val:X})");
        }
        else Console.WriteLine($"  {fname}");
    }

    Console.WriteLine("  Methods:");
    int idx = 0;
    foreach (var mh in td.GetMethods())
    {
        var m     = mr.GetMethodDefinition(mh);
        var mname = mr.GetString(m.Name);
        try   { Console.WriteLine($"    [{idx++}] {mname}: {DecodeSig(mr.GetBlobBytes(m.Signature))}"); }
        catch (Exception ex) { Console.WriteLine($"    [{idx++}] {mname}: ERR {ex.Message}"); }
    }
    Console.WriteLine();
}
