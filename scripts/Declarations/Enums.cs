using Godot;
using System;

[GlobalClass]
public partial class Enums : RefCounted
{
	// States retain implicit numbering after an explicit value.
	public enum TokenState
	{
		Empty,
		// Negative values and aliases are allowed.
		Pending = -2,
		Next,

		Parsed = 4,
		Complete,
		Alias = Parsed,
		Minimum = -2147483648,
		Maximum = 2147483647,
	}

	public enum AccessMode
	{
		None, Read = 1,
		Write = 2, ReadWrite = Read | Write
	}

	public const TokenState Initial = TokenState.Empty;
	public TokenState Current = TokenState.Parsed;
	public TokenState Unset;
	public Godot.Collections.Array<TokenState> States = new Godot.Collections.Array<TokenState>();
	public Entry.Phase Phase = Entry.Phase.Ready;
	public TokenState Numeric = (TokenState)(4);

	public TokenState select(TokenState state = TokenState.Empty)
	{
		TokenState previous = default;
		var next = TokenState.Complete;
		previous = Current;
		if (state == TokenState.Empty)
		{
			Current = next;
		}
		else
		{
			Current = state;
		}
		return previous;
	}

	public TokenState convert(int value)
	{
		var state = (TokenState)(value);
		var number = (int)(state);
		return (TokenState)(number);
	}

	public partial class Entry : RefCounted
	{
		// Nested enum names belong to their declaring class.
		public enum Phase { Ready, Done = 3 }

		public Phase State = Phase.Ready;

		public Phase advance(Phase value)
		{
			State = value;
			return State;
		}
	}
}
