#nullable enable
using System;
using System.Text;
using System.Text.RegularExpressions;
using Terraria.ModLoader.IO;

namespace GregTechCEuTerraria.Api.Cover.Filter;

// Port of com.gregtechceu.gtceu.api.cover.filter.TagFilter<T, S> - the shared
// base of TagItemFilter / TagFluidFilter.
//
// A tag filter matches a resource against a boolean expression over its tag ids
// (see TagExprFilter), e.g. "forge:ingots/iron & !forge:dusts/*".
//
// Documented adaptations:
//   - The recursive <T, S> generics are dropped (see IFilter); the two concrete
//     subclasses implement IItemFilter / IFluidFilter directly.
//   - openConfigurator (an LDLib TextFieldWidget) is dropped - the widget tree
//     is rebuilt Terraria-side (CoverSettingsUI). Its input validator IS ported
//     verbatim, as the static NormalizeExpression below, so the Terraria text
//     field runs the exact same operator-collapse + paren-balance pass.
public abstract class TagFilter
{
	public string OreDictFilterExpression { get; protected set; } = "";
	protected TagExprFilter.MatchExpr? MatchExpr;

	public Action OnUpdated { get; set; } = () => { };

	// Tag filters are never blacklists (verbatim - upstream TagFilter does not
	// override Filter.isBlackList).
	public bool IsBlackList => false;

	public bool IsBlank => string.IsNullOrWhiteSpace(OreDictFilterExpression);

	public TagCompound? SaveFilter()
	{
		if (IsBlank) return null;
		return new TagCompound { ["oreDict"] = OreDictFilterExpression };
	}

	public virtual void SetOreDict(string oreDict)
	{
		OreDictFilterExpression = oreDict;
		MatchExpr = TagExprFilter.ParseExpression(oreDict);
		OnUpdated();
	}

	protected void LoadOreDict(TagCompound tag)
	{
		OreDictFilterExpression = tag.ContainsKey("oreDict") ? tag.GetString("oreDict") : "";
		MatchExpr = TagExprFilter.ParseExpression(OreDictFilterExpression);
	}

	// Verbatim port of the TextFieldWidget validator inside upstream
	// TagFilter.openConfigurator - collapses doubled operators / spaces and
	// balances parentheses (moving operators so e.g. "xxx (& yyy)" becomes
	// "xxx & (yyy)"). Extracted as a static so the Terraria-side commit-style
	// text field can run it on the entered expression before SetOreDict.
	private static readonly Regex DoubleWildcard = new(@"\*{2,}");
	private static readonly Regex DoubleAnd      = new(@"&{2,}");
	private static readonly Regex DoubleOr       = new(@"\|{2,}");
	private static readonly Regex DoubleNot      = new(@"!{2,}");
	private static readonly Regex DoubleXor      = new(@"\^{2,}");
	private static readonly Regex DoubleSpace    = new(@" {2,}");

	public static string NormalizeExpression(string input)
	{
		input ??= "";
		// remove all operators that are double
		input = DoubleWildcard.Replace(input, "*");
		input = DoubleAnd.Replace(input, "&");
		input = DoubleOr.Replace(input, "|");
		input = DoubleNot.Replace(input, "!");
		input = DoubleXor.Replace(input, "^");
		input = DoubleSpace.Replace(input, " ");

		// move ( and ) so it doesn't create invalid expressions f.e. xxx (& yyy)
		// => xxx & (yyy); append or prepend ( and ) if the amount is not equal
		var builder = new StringBuilder();
		int unclosed = 0;
		char last = ' ';
		for (int i = 0; i < input.Length; i++)
		{
			char c = input[i];
			if (c == ' ')
			{
				if (last != '(')
					builder.Append(' ');
				continue;
			}
			if (c == '(')
			{
				unclosed++;
			}
			else if (c == ')')
			{
				unclosed--;
				if (last == '&' || last == '|' || last == '^')
				{
					string s = builder.ToString();
					int l = s.LastIndexOf(" " + last, StringComparison.Ordinal);
					int l2 = s.LastIndexOf(last);
					builder.Insert(l == l2 - 1 ? l : l2, ")");
					continue;
				}
				if (i > 0 && builder.Length > 0 && builder[builder.Length - 1] == ' ')
				{
					builder.Remove(builder.Length - 1, 1);
				}
			}
			else if ((c == '&' || c == '|' || c == '^') && last == '(')
			{
				builder.Remove(builder.ToString().LastIndexOf('('), 1);
				builder.Append(c).Append(" (");
				continue;
			}

			builder.Append(c);
			last = c;
		}
		if (unclosed > 0)
		{
			builder.Append(new string(')', unclosed));
		}
		else if (unclosed < 0)
		{
			unclosed = -unclosed;
			for (int i = 0; i < unclosed; i++)
				builder.Insert(0, '(');
		}
		input = builder.ToString();
		input = DoubleSpace.Replace(input, " ");
		return input;
	}
}
