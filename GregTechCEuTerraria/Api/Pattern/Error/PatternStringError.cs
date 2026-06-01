#nullable enable
using Terraria.Localization;

namespace GregTechCEuTerraria.Api.Pattern.Error;

// LOCKED - verbatim port of
// com.gregtechceu.gtceu.api.pattern.error.PatternStringError.
//
// A pattern error whose display text is a translation key. Used by the
// in-progress / unloaded-chunk markers on `MultiblockState` and by any caller
// that wants a static "Something specific went wrong" message.
public class PatternStringError : PatternError
{
	public string TranslateKey { get; }

	public PatternStringError(string translateKey)
	{
		TranslateKey = translateKey;
	}

	public override string ErrorInfo => Language.GetTextValue(TranslateKey);
}
