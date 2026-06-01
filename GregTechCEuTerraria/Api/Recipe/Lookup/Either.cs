#nullable enable
using System;

namespace GregTechCEuTerraria.Api.Recipe.Lookup;

// Minimal port of com.mojang.datafixers.util.Either - only the
// left/right discriminated-union surface RecipeDB + Branch need. Upstream
// pulls Either from Mojang's DataFixerUpper; we have no such dependency, so
// this carries just the Left/Right discriminator plus value accessors.
//
// Upstream call-site translation (kept greppable against RecipeDB.java):
//   Either.left(x) / Either.right(y)   -> Either<,>.Left(x) / .Right(y)
//   either.left().isPresent()          -> either.IsLeft
//   either.left().isEmpty()            -> !either.IsLeft  (i.e. IsRight)
//   either.left().get()                -> either.LeftValue
//   either.right().isPresent()         -> either.IsRight
//   either.right().get()               -> either.RightValue
internal sealed class Either<TL, TR>
	where TL : class
	where TR : class
{
	private readonly TL? _left;
	private readonly TR? _right;

	public bool IsLeft  { get; }
	public bool IsRight => !IsLeft;

	private Either(bool isLeft, TL? left, TR? right)
	{
		IsLeft = isLeft;
		_left  = left;
		_right = right;
	}

	public static Either<TL, TR> Left(TL value)  => new(true,  value, null);
	public static Either<TL, TR> Right(TR value) => new(false, null,  value);

	public TL LeftValue  => IsLeft  ? _left!  : throw new InvalidOperationException("Either is Right");
	public TR RightValue => IsRight ? _right! : throw new InvalidOperationException("Either is Left");
}
