using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;

namespace Orts.Formats.Msts.Signalling
{
    #region Script Tokens
    internal class ScriptToken
    {
        protected static int indent;

        public virtual string Token { get; set; }

        public override string ToString()
        {
            return Token;
        }
    }

    internal class OperatorToken : ScriptToken
    {
        public OperatorToken(string token, int lineNumber)
        {
            Token = token;

            switch (token)
            {
                case "NOT":
                case "!":
                    OperatorType = OperatorType.Negator;
                    break;
                case "AND":
                case "OR":
                case "||":
                case "&&":
                case "EOR":
                case "^":
                    OperatorType = OperatorType.Logical;
                    break;
                case "=":
                case "#=":
                case "+=":
                case "-=":
                case "*=":
                case "/=":
                case "/#=":
                case "%=":
                    OperatorType = OperatorType.Assignment;
                    break;
                case "-":
                case "*":
                case "+":
                case "/":
                case "/#":
                case "%":
                case "DIV":
                case "MOD":
                    OperatorType = OperatorType.Operation;
                    break;
                case ">":
                case ">#":
                case ">=":
                case ">=#":
                case "<":
                case "<#":
                case "<=":
                case "<=#":
                case "==":
                case "==#":
                case "!=":
                case "!=#":
                    OperatorType = OperatorType.Equality;
                    break;
                default:
                    OperatorType = OperatorType.Other;
                    Trace.TraceWarning($"sigscr-file : Invalid operator token {token} in line number {lineNumber}");
                    break;
            }
        }

        public OperatorType OperatorType { get; private set; }
    }

    internal abstract class BlockBase : ScriptToken
    {

        protected static int blockID;

        protected int BlockId = blockID++;

        protected int returnBlockId;

        public BlockBase Parent { get; private set; }

        public int LineNumber { get; set; }

        public List<ScriptToken> Tokens { get; } = new List<ScriptToken>();

        public BlockBase Current { get; protected set; }

        public BlockBase(BlockBase parent, int lineNumber)
        {
            Parent = parent;
            Parent?.AddNestedBlock(this);
            LineNumber = lineNumber;
        }

        protected void AddNestedBlock(BlockBase block)
        {
            Current = block;
            Tokens.Add(block);
        }

        protected BlockBase FindAlternateReturn(int returnBlockId)
        {
            return returnBlockId == BlockId ? this : Parent.FindAlternateReturn(returnBlockId);
        }

        public override string Token => ToString();

        public override string ToString()
        {
            if (Tokens.Count == 0)
                return string.Empty;
            StringBuilder builder = new StringBuilder();
            indent++;

            foreach (ScriptToken block in Tokens)
            {
                builder.Append(block.ToString());
                if (builder[builder.Length - 1] != '\n')
                    builder.AppendLine();
            }
            if (builder.Length > 3)
                builder.Length -= 2;
            indent--;
            return builder.ToString();
        }

        public virtual BlockBase Add(ScriptToken token, int lineNumber)
        {
            Current = Current ?? new Statement(this, lineNumber);
            //token should be Value or Operator only added to current statement or enclosing
            Current.Tokens.Add(token);
            return Current;
        }

        public virtual BlockBase StartBlock(int lineNumber)
        { return new Block(this, lineNumber); }

        public virtual BlockBase StartEnclosure(int lineNumber)
        { return new Enclosure(this, lineNumber); }

        public virtual BlockBase StartCondition(int lineNumber)
        { return new ConditionalBlock(this, lineNumber); }

        public virtual BlockBase StartAlternate(int lineNumber)
        {
            if (Tokens.Last() is ConditionalBlock child)
            {
                child.returnBlockId = this is ConditionalBlock ? returnBlockId : BlockId;
                BlockBase result = child.RequiresAlternate();
                if (result != null)
                {
                    result.returnBlockId = this is ConditionalBlock ? returnBlockId : BlockId;
                    result = result.StartAlternate(lineNumber);
                    (result as ConditionalBlock).AcceptAlternate();
                    return result;
                }
                return this;
            }
            return this as ConditionalBlock;
        }

        public virtual BlockBase CompleteBlock()
        {
            if (Parent is ConditionalBlock)
            {
                return Parent.CompleteBlock();
            }
            else
            {
                Current = null;
                Parent.Current = null;
                return Parent;
            }
        }
    }

    internal class Script : Block
    {
        public Script(int lineNumber) : base(null, lineNumber)
        { }

        public string ScriptName { get; set; } = string.Empty;

        public override string ToString()
        {
            return $"Script {ScriptName}\r\n" + base.ToString();
        }
    }

    internal class Statement : BlockBase
    {
        public Statement(BlockBase parent, int lineNumber) : base(parent, lineNumber)
        { }

        public override BlockBase Add(ScriptToken token, int lineNumber)
        {
            Tokens.Add(token);
            return this;
        }

        public override string ToString()
        {
            if (Tokens.Count == 0)
                return string.Empty;
            StringBuilder builder = new StringBuilder();
            builder.Append(' ', indent);
            foreach (ScriptToken token in Tokens)
            {
                builder.Append(token.ToString());
                builder.Append(' ');
            }
            if (builder.Length > 1)
                builder[builder.Length - 1] = ';';
            return builder.ToString();
        }
    }

    internal class Block : BlockBase
    {
        public Block(BlockBase parent, int lineNumber) : base(parent, lineNumber)
        { }

        public override string ToString()
        {
            return $"{new string(' ', indent)}{{\r\n{base.ToString()}\r\n{new string(' ', indent)}}}\r\n";
        }
    }

    internal class Enclosure : BlockBase
    {
        public Enclosure(BlockBase parent, int lineNumber) : base(parent, lineNumber)
        { }

        public override BlockBase Add(ScriptToken token, int lineNumber)
        {
            Tokens.Add(token);
            return this;
        }

        public override string ToString()
        {
            if (Tokens.Count == 0)
                return "()";
            StringBuilder builder = new StringBuilder();
            builder.Append('(');
            foreach (ScriptToken token in Tokens)
            {
                string statement = token.ToString().TrimEnd('\r', '\n');
                builder.Append(statement);
                builder.Append(' ');
            }
            if (builder.Length > 1)
                builder.Length--;
            builder.Append(')');
            return builder.ToString();
        }
    }

    internal class ConditionalBlock : BlockBase
    {
        private ConditionState state = ConditionState.Started;

        public ConditionalBlock(BlockBase parent, int lineNumber) : base(parent, lineNumber)
        { }

        internal bool IsAlternateCondition { get; private set; }

        internal bool HasAlternate => state == ConditionState.HasAlternate;

        public override BlockBase Add(ScriptToken token, int lineNumber)
        {
            return base.Add(token, lineNumber);
        }

        public override BlockBase CompleteBlock()
        {
            BlockBase result;
            if (state == ConditionState.Started && Current is Enclosure)
            {
                state = ConditionState.HasCondition;
                result = this;
            }
            else if (state == ConditionState.HasCondition && (Current is Statement || Current is Block || Current is ConditionalBlock))
            {
                state = ConditionState.HasStatement;
                result = base.CompleteBlock();
            }
            else if (state == ConditionState.HasAlternateCondition)
            {
                result = FindAlternateReturn(returnBlockId);
            }
            else if (state == ConditionState.ExpectingAlternate && (Current is Statement || Current is Block || Current is ConditionalBlock))
            {
                state = ConditionState.HasAlternate;
                result = FindAlternateReturn(returnBlockId);
            }
            else
                result = null;
            Current = null;
            return result;
        }

        public override BlockBase StartCondition(int lineNumber)
        {
            ConditionalBlock result = base.StartCondition(lineNumber) as ConditionalBlock;
            if (state == ConditionState.ExpectingAlternate) //this is the If after Else for ElseIf
            {
                state = ConditionState.HasAlternateCondition; //mark myself to have ElseIf
                result.IsAlternateCondition = true; //mark as ElseIf Condition
            }   //else this is just a nested Condition If () If () ;
            return result;
        }

        public BlockBase RequiresAlternate()
        {
            if (state == ConditionState.HasAlternate || IsAlternateCondition)
            {
                if (Tokens.ElementAtOrDefault(1) is ConditionalBlock child)
                    return child.RequiresAlternate();
                else
                    return null;
            }
            return this;
        }

        public void AcceptAlternate()
        {
            state = ConditionState.ExpectingAlternate;
        }

        public override string ToString()
        {
            StringBuilder builder = new StringBuilder();
            //builder.Append($"{BlockId}");
            builder.Append(' ', indent);
            builder.Append(IsAlternateCondition ? "ELSE IF " : "IF ");
            builder.AppendLine(Tokens.First().ToString());

            int index = 0;
            while (index++ < Tokens.Count - 1)
            {
                if (state == ConditionState.HasAlternate && index == Tokens.Count - 1)  //the last block is the alternate
                    builder.AppendLine($"{new string(' ', indent)}ELSE");
                builder.Append(AdjustIndentAndPrint(Tokens[index] as BlockBase));
                if (builder[builder.Length - 1] != '\n')
                    builder.AppendLine();
            }
            builder.Length -= 2;//remove last CRLF
            //builder.Append($"?{BlockId}");
            return builder.ToString();

            string AdjustIndentAndPrint(BlockBase block)
            {
                string result;
                if (block is Statement || (block is ConditionalBlock && !(block as ConditionalBlock).IsAlternateCondition)) //Single statements or Nested Conditions (but not ElseIf)
                {
                    indent++;
                    result = block.ToString();
                    indent--;
                }
                else
                    result = block.ToString();

                return result;
            }
        }
    }
    #endregion

}
