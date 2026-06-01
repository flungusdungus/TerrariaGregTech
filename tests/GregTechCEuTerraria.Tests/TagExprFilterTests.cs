using System.Collections.Generic;
using GregTechCEuTerraria.Api.Cover.Filter;
using Xunit;

namespace GregTechCEuTerraria.Tests;

// Covers the tag-filter expression parser/evaluator ported from upstream's
// TagExprFilter - operators, wildcards, the bare-term forge: prefix, grouping.
public class TagExprFilterTests
{
	private static bool Match(string expr, params string[] tags) =>
		TagExprFilter.TagsMatch(TagExprFilter.ParseExpression(expr), new HashSet<string>(tags));

	[Fact]
	public void SingleTag_MatchesWhenPresent()
	{
		Assert.True(Match("forge:ingots/iron", "forge:ingots/iron"));
		Assert.False(Match("forge:ingots/iron", "forge:ingots/gold"));
	}

	[Fact]
	public void BareTerm_GetsForgePrefix()
	{
		// "ingots/iron" has no namespace -> treated as "forge:ingots/iron".
		Assert.True(Match("ingots/iron", "forge:ingots/iron"));
		Assert.False(Match("ingots/iron", "gtceu:ingots/iron"));
	}

	[Fact]
	public void And()
	{
		Assert.True(Match("a:x & a:y", "a:x", "a:y"));
		Assert.False(Match("a:x & a:y", "a:x"));
	}

	[Fact]
	public void Or()
	{
		Assert.True(Match("a:x | a:y", "a:y"));
		Assert.False(Match("a:x | a:y", "a:z"));
	}

	[Fact]
	public void Not()
	{
		Assert.True(Match("!a:x", "a:y"));
		Assert.False(Match("!a:x", "a:x"));
	}

	[Fact]
	public void Xor()
	{
		Assert.True(Match("a:x ^ a:y", "a:x"));
		Assert.False(Match("a:x ^ a:y", "a:x", "a:y"));
		Assert.False(Match("a:x ^ a:y", "a:z"));
	}

	[Fact]
	public void Wildcard()
	{
		Assert.True(Match("forge:ingots/*", "forge:ingots/iron"));
		Assert.False(Match("forge:ingots/*", "forge:dusts/iron"));
	}

	[Fact]
	public void Grouping()
	{
		Assert.True(Match("(a:x | a:y) & a:z", "a:y", "a:z"));
		Assert.False(Match("(a:x | a:y) & a:z", "a:y"));
	}

	[Fact]
	public void Dollar_MatchesEmptyTagSet()
	{
		Assert.True(Match("$"));
		Assert.False(Match("$", "a:x"));
	}

	[Fact]
	public void EmptyExpression_NeverMatches()
	{
		Assert.False(TagExprFilter.TagsMatch(TagExprFilter.ParseExpression(""),
			new HashSet<string> { "a:x" }));
	}
}
