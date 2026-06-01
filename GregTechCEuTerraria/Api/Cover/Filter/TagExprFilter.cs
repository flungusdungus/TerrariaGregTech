#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace GregTechCEuTerraria.Api.Cover.Filter;

// Port of com.gregtechceu.gtceu.utils.TagExprFilter.
//
// Parses a tag-filter expression - e.g. "forge:ingots/iron & !forge:dusts/*" -
// into a MatchExpr tree, then evaluates it against the set of tag ids a
// resource carries. Operators: & and, | or, ^ xor, ! not, ( ) grouping,
// * wildcard. A bare term with no namespace is prefixed "forge:"; "$" matches
// the empty tag set.
//
// Verbatim port of the tokenizer + recursive-descent parser. The one
// adaptation: upstream's tagsMatch(expr, ItemStack / FluidStack) collected the
// stack's tags through the Minecraft tag system. Terraria has no such system,
// so the caller resolves the tag set (via TagSource) and TagsMatch just takes
// the Set<string>. Pattern.quote -> Regex.Escape; Pattern.matches (full match)
// -> an anchored Regex.IsMatch.
public static class TagExprFilter
{
	public enum TokenType { LParen, RParen, And, Or, Not, Xor, String }

	public sealed class Token
	{
		public string? Lexeme;
		public TokenType Type;

		public Token(TokenType type) => Type = type;
		public Token(TokenType type, string lexeme) { Type = type; Lexeme = lexeme; }
	}

	public abstract class MatchExpr
	{
		public abstract bool Matches(IReadOnlyCollection<string> input);
	}

	private sealed class BinExpr : MatchExpr
	{
		private readonly MatchExpr? _left, _right;
		private readonly Token _op;

		public BinExpr(Token op, MatchExpr? left, MatchExpr? right)
		{
			_op = op;
			_left = left;
			_right = right;
		}

		public override bool Matches(IReadOnlyCollection<string> input)
		{
			if (_left == null || _right == null) return false;
			return _op.Type switch
			{
				TokenType.And => _left.Matches(input) && _right.Matches(input),
				TokenType.Or => _left.Matches(input) || _right.Matches(input),
				TokenType.Xor => _left.Matches(input) ^ _right.Matches(input),
				_ => false,
			};
		}
	}

	private sealed class UnaryExpr : MatchExpr
	{
		private readonly Token _token;
		private readonly MatchExpr? _expr;

		public UnaryExpr(Token token, MatchExpr? expr) { _token = token; _expr = expr; }

		public override bool Matches(IReadOnlyCollection<string> input) =>
			_token.Type == TokenType.Not && _expr != null && !_expr.Matches(input);
	}

	private sealed class StringExpr : MatchExpr
	{
		private string? _value;

		public StringExpr(string? value) => _value = value;

		public override bool Matches(IReadOnlyCollection<string> input)
		{
			if (string.IsNullOrEmpty(_value)) return false;
			if (_value == "$" && input.Count == 0) return true;
			if (!_value.Contains(':') && !_value.StartsWith("*"))
				_value = "forge:" + _value;

			string val = Quote(_value);
			return input.Any(inp => Regex.IsMatch(inp, "^(?:" + val + ")$"));
		}

		private static string Quote(string str)
		{
			if (str.Contains('*'))
			{
				int idx = str.IndexOf('*');
				if (idx == str.Length - 1)
					return Quote(str.Substring(0, idx)) + ".*";
				return Quote(str.Substring(0, idx)) + ".*" + Quote(str.Substring(idx + 1));
			}
			return Regex.Escape(str);
		}
	}

	private sealed class GroupingExpr : MatchExpr
	{
		private readonly MatchExpr? _inner;

		public GroupingExpr(MatchExpr? inner) => _inner = inner;

		public override bool Matches(IReadOnlyCollection<string> input) => _inner != null && _inner.Matches(input);
	}

	private sealed class Parser
	{
		private List<Token> _tokens = new();
		private int _idx;
		private Token? _prev;

		public MatchExpr? Parse(string expr)
		{
			_tokens = Tokenize(expr);
			_idx = 0;
			return Expression();
		}

		private bool Match(TokenType tt)
		{
			if (_idx >= _tokens.Count) return false;
			if (_tokens[_idx].Type == tt)
			{
				_prev = _tokens[_idx];
				_idx++;
				return true;
			}
			return false;
		}

		private MatchExpr? Expression() => Term();

		private MatchExpr? Term()
		{
			MatchExpr? lhs = Unary();

			BinExpr? result = null;
			while (Match(TokenType.And) || Match(TokenType.Or) || Match(TokenType.Xor))
			{
				result = result == null
					? new BinExpr(_prev!, lhs, Unary())
					: new BinExpr(_prev!, result, Unary());
			}
			return result ?? lhs;
		}

		private MatchExpr? Unary()
		{
			if (Match(TokenType.Not)) return new UnaryExpr(_prev!, Id());
			return Id();
		}

		private MatchExpr? Id()
		{
			if (Match(TokenType.LParen))
			{
				MatchExpr? inner = Expression();
				Match(TokenType.RParen);
				return new GroupingExpr(inner);
			}
			if (Match(TokenType.String))
				return new StringExpr(_prev!.Lexeme);
			return null;
		}

		private static List<Token> Tokenize(string expr)
		{
			var result = new List<Token>();
			int idx = 0;
			while (idx < expr.Length)
			{
				char cur = expr[idx];
				if (char.IsWhiteSpace(cur)) { idx++; continue; }

				// Parse strings
				int stringLen = 0;
				while (cur != '(' && cur != ')' && cur != '!' && cur != '&' && cur != '|' && cur != '^' &&
				       cur != ' ')
				{
					stringLen++;
					if (stringLen + idx == expr.Length) break;
					cur = expr[idx + stringLen];
				}
				if (stringLen > 0)
				{
					result.Add(new Token(TokenType.String, expr.Substring(idx, stringLen)));
					idx += stringLen;
					continue;
				}

				// Parse operators
				switch (cur)
				{
					case '!': result.Add(new Token(TokenType.Not)); break;
					case '&': result.Add(new Token(TokenType.And)); break;
					case '|': result.Add(new Token(TokenType.Or)); break;
					case '^': result.Add(new Token(TokenType.Xor)); break;
					case '(': result.Add(new Token(TokenType.LParen)); break;
					case ')': result.Add(new Token(TokenType.RParen)); break;
				}
				idx++;
			}
			return result;
		}
	}

	// Parse an expression into a MatchExpr tree (null for an empty expression).
	public static MatchExpr? ParseExpression(string expression) => new Parser().Parse(expression);

	// Evaluate a parsed expression against a resource's tag-id set.
	public static bool TagsMatch(MatchExpr? expr, IReadOnlyCollection<string> tags) => expr != null && expr.Matches(tags);
}
