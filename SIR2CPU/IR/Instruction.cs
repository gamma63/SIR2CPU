namespace SIR2CPU.IR;

public record Instruction(OpCode OpCode, object? Operand1, object? Operand2, Condition? Condition);