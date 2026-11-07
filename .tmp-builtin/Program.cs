using dotnes.ObjectModel;
var p = Program6502.CreateWithBuiltIns();
p.ResolveAddresses();
Console.WriteLine($"stockCRT={p.TotalSize} bytes, ends=0x{0x8000+p.TotalSize:X4}");
Console.WriteLine($"GetBuiltInSize={Program6502.GetBuiltInSize()}");
var fin = Program6502.CalculateFinalBuiltInsSize(0, new HashSet<string>(), null);
Console.WriteLine($"finalBuiltInsMin={fin}");
