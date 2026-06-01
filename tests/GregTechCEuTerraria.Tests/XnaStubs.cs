// Minimal stand-ins for Microsoft.Xna.Framework.Color + Vector3.
//
// VoltageTier.cs (Common/Energy/VoltageTier.cs) is otherwise pure logic but
// exposes two convenience methods - VoltageTiers.TextColor (returns an XNA
// Color) and LightColor (returns an XNA Vector3). The test project cannot
// reference Microsoft.Xna.Framework (it's a tModLoader-only dependency).
// Rather than drop every VoltageTier-dependent test, we supply byte/float-
// compatible stubs so VoltageTier.cs compiles unchanged.
//
// The stubs are never exercised by any test - they only need to satisfy the
// compiler for the TextColor / LightColor signatures.
namespace Microsoft.Xna.Framework;

public readonly struct Color
{
	public byte R { get; }
	public byte G { get; }
	public byte B { get; }
	public byte A { get; }

	public Color(byte r, byte g, byte b)
	{
		R = r; G = g; B = b; A = 255;
	}

	public Color(byte r, byte g, byte b, byte a)
	{
		R = r; G = g; B = b; A = a;
	}
}

public readonly struct Vector3
{
	public float X { get; }
	public float Y { get; }
	public float Z { get; }

	public Vector3(float x, float y, float z)
	{
		X = x; Y = y; Z = z;
	}
}
