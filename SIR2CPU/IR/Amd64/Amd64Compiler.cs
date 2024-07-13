using SIR2CPU.Base;
using SIR2CPU.Linker;
using static SIR2CPU.IR.OpCode;

namespace SIR2CPU.IR.Amd64;

public class Amd64Compiler : Compiler
{
    private readonly string _asmPath, _binPath;

    public Amd64Compiler(ref IRCompiler compiler) : base(ref compiler)
    {
        _asmPath = Path.ChangeExtension(compiler.Settings.OutputFile, "asm");
        _binPath = Path.ChangeExtension(compiler.Settings.OutputFile, "bin");
        OutputPath = Path.ChangeExtension(compiler.Settings.OutputFile, "elf");
    }

    public override void Initialize()
    {
        if (IRCompiler.Settings.ImageType != ImageType.None)
        {
            Builder.AppendLine("[bits 32]");

            Builder.AppendLine("KERNEL_STACK equ 0x00200000");

            // Thanks https://os.phil-opp.com/multiboot-kernel!
            Builder.AppendLine("dd 0xE85250D6"); // Magic
            Builder.AppendLine("dd 0"); // Architecture
            Builder.AppendLine("dd 16"); // Header length
            Builder.AppendLine("dd 0x100000000-(0xE85250D6+16)"); // Checksum
            // Required tag
            Builder.AppendLine("dw 0");
            Builder.AppendLine("dw 0");
            Builder.AppendLine("dd 8");

            Builder.AppendLine("mov esp,KERNEL_STACK");
            Builder.AppendLine("push 0");
            Builder.AppendLine("popf");
            Builder.AppendLine("push eax");
            Builder.AppendLine("push 0");
            Builder.AppendLine("push ebx");
            Builder.AppendLine("push 0");
            Builder.AppendLine("call EnterLongMode");

            Builder.AppendLine("align 4");
            Builder.AppendLine("IDT:");
            Builder.AppendLine(".Length dw 0");
            Builder.AppendLine(".Base dd 0");

            Builder.AppendLine("EnterLongMode:");
            Builder.AppendLine("mov edi,p4_table");
            Builder.AppendLine("push di");
            Builder.AppendLine("mov eax,p3_table");
            Builder.AppendLine("or eax,3");
            Builder.AppendLine("mov [p4_table],eax");
            Builder.AppendLine("mov eax,p2_table");
            Builder.AppendLine("or eax,3");
            Builder.AppendLine("mov [p3_table],eax");
            Builder.AppendLine("mov ecx,0");

            Builder.AppendLine(".Map_P2_Table:");
            Builder.AppendLine("mov eax,0x200000");
            Builder.AppendLine("mul ecx");
            Builder.AppendLine("or eax,131");
            Builder.AppendLine("mov [p2_table+ecx*8],eax");
            Builder.AppendLine("inc ecx");
            Builder.AppendLine("cmp ecx,512");
            Builder.AppendLine("jne .Map_P2_Table");

            Builder.AppendLine("pop di");
            Builder.AppendLine("mov al,0xFF");
            Builder.AppendLine("out 0xA1,al");
            Builder.AppendLine("out 0x21,al");
            Builder.AppendLine("cli");
            Builder.AppendLine("nop");
            Builder.AppendLine("nop");
            Builder.AppendLine("lidt [IDT]");
            Builder.AppendLine("mov eax,160");
            Builder.AppendLine("mov cr4,eax");
            Builder.AppendLine("mov edx,edi");
            Builder.AppendLine("mov cr3,edx");
            Builder.AppendLine("mov ecx,0xC0000080");
            Builder.AppendLine("rdmsr");
            Builder.AppendLine("or eax,0x00000100");
            Builder.AppendLine("wrmsr");
            Builder.AppendLine("mov ebx,cr0");
            Builder.AppendLine("or ebx,0x80000001");
            Builder.AppendLine("mov cr0,ebx");
            Builder.AppendLine("lgdt [GDT.Pointer]");
            Builder.AppendLine("sti");
            Builder.AppendLine("mov ax,0x0010");
            Builder.AppendLine("mov ds,ax");
            Builder.AppendLine("mov es,ax");
            Builder.AppendLine("mov fs,ax");
            Builder.AppendLine("mov gs,ax");
            Builder.AppendLine("mov ss,ax");
            Builder.AppendLine("jmp 0x0008:Main");

            Builder.AppendLine("GDT:");
            Builder.AppendLine(".Null:");
            Builder.AppendLine("dq 0x0000000000000000");
            Builder.AppendLine(".Code:");
            Builder.AppendLine("dq 0x00209A0000000000");
            Builder.AppendLine("dq 0x0000920000000000");
            Builder.AppendLine("align 4");
            Builder.AppendLine("dw 0");
            Builder.AppendLine(".Pointer:");
            Builder.AppendLine("dw $-GDT-1");
            Builder.AppendLine("dd GDT");

            Builder.AppendLine("align 4096");
            Builder.AppendLine("p4_table:");
            Builder.AppendLine("resb 4096");
            Builder.AppendLine("p3_table:");
            Builder.AppendLine("resb 4096");
            Builder.AppendLine("p2_table:");
            Builder.AppendLine("resb 4096");

            Builder.AppendLine("[bits 64]");
            Builder.AppendLine("Main:");
            Builder.AppendLine("pop rsi");
            Builder.AppendLine("pop rdx");
            Builder.AppendLine("mov rbp,KERNEL_STACK-1024");

            return;
        }

        Builder.AppendLine("[bits 64]");
    }

    public override void Compile()
    {
        // Create virtual registers
        for (var i = 0; i < 4; i++)
        {
            Builder.Append($"Register{i}:dq 0");
            for (var j = 0; j < 29; j++)
                Builder.Append(",0");
            Builder.AppendLine();
        }

        var calls = 0;
        foreach (var (opCode, op, src, condition) in IRCompiler.Builder.Instructions)
        {
            switch (opCode)
            {
                case Dup:
                    Builder.AppendLine("push qword [rsp]");
                    break;
                case Label:
                    Builder.AppendLine(op + ":");
                    break;
                case Popd:
                    Builder.AppendLine("add rsp,4");
                    break;
                
                case Jmp:
                    switch (condition)
                    {
                        case Condition.Zero:
                            Builder.AppendLine("pop rcx"); // Value
                            Builder.AppendLine("cmp rcx,0");
                            Builder.AppendLine("jz " + op);
                            break;
                        
                        case Condition.NotZero:
                            Builder.AppendLine("pop rcx"); // Value
                            Builder.AppendLine("cmp rcx,0");
                            Builder.AppendLine("jnz " + op);
                            break;
                        
                        case Condition.Less:
                            Builder.AppendLine("pop rcx"); // Value 2
                            Builder.AppendLine("pop rdx"); // Value 1
                            Builder.AppendLine("cmp rdx,rcx");
                            // Shouldn't this be jl?
                            Builder.AppendLine("jb " + op);
                            break;
                        
                        case Condition.NotEqual:
                            Builder.AppendLine("pop rcx"); // Value 2
                            Builder.AppendLine("pop rdx"); // Value 1
                            Builder.AppendLine("cmp rdx,rcx");
                            Builder.AppendLine("jne " + op);
                            break;
                        
                        case Condition.Equal:
                            Builder.AppendLine("pop rcx"); // Value 2
                            Builder.AppendLine("pop rdx"); // Value 1
                            Builder.AppendLine("cmp rdx,rcx");
                            Builder.AppendLine("je " + op);
                            break;
                        
                        default:
                            Builder.AppendLine("jmp " + op);
                            break;
                    }
                    break;

                case Push:
                    switch (condition)
                    {
                        case Condition.Zero:
                            Builder.AppendLine("xor rax,rax");
                            Builder.AppendLine("pop rcx"); // Value 2
                            Builder.AppendLine("pop rdx"); // Value 1
                            Builder.AppendLine("cmp rdx,rcx");
                            Builder.AppendLine("setz al");
                            Builder.AppendLine("push rax");
                            break;
                        
                        case Condition.NotZero:
                            Builder.AppendLine("xor rax,rax");
                            Builder.AppendLine("pop rcx"); // Value 2
                            Builder.AppendLine("pop rdx"); // Value 1
                            Builder.AppendLine("cmp rdx,rcx");
                            Builder.AppendLine("setnz al");
                            Builder.AppendLine("push rax");
                            break;
                        
                        case Condition.Less:
                            Builder.AppendLine("xor rax,rax");
                            Builder.AppendLine("pop rcx"); // Value 2
                            Builder.AppendLine("pop rdx"); // Value 1
                            Builder.AppendLine("cmp rdx,rcx");
                            Builder.AppendLine("setl al");
                            Builder.AppendLine("push rax");
                            break;
                        
                        case Condition.NotEqual:
                            Builder.AppendLine("xor rax,rax");
                            Builder.AppendLine("pop rcx"); // Value 2
                            Builder.AppendLine("pop rdx"); // Value 1
                            Builder.AppendLine("cmp rdx,rcx");
                            Builder.AppendLine("setne al");
                            Builder.AppendLine("push rax");
                            break;
                        
                        case Condition.Equal:
                            Builder.AppendLine("xor rax,rax");
                            Builder.AppendLine("pop rcx"); // Value 2
                            Builder.AppendLine("pop rdx"); // Value 1
                            Builder.AppendLine("cmp rdx,rcx");
                            Builder.AppendLine("sete al");
                            Builder.AppendLine("push rax");
                            break;
                        
                        default:
                            Builder.AppendLine("push " + (op is Register r ? $"qword [Register{r.Index}+{r.Value * 8}]" : op));
                            break;
                    }
                    break;

                case Pop:
                {
                    if (op is not Register r)
                        throw new Exception("What the fuck are you trying to do? Pop to an immediate value????");
                    
                    Builder.AppendLine($"pop qword [Register{r.Index}+{r.Value * 8}]");
                    break;
                }

                case Mov:
                {
                    if (op is not Register dest)
                        throw new Exception("What the fuck are you trying to do? Mov to an immediate value????");

                    Builder.AppendLine($"mov qword [Register{dest.Index}+{dest.Value * 8}],{(src is Register r ? $"qword [Register{r.Index}+{r.Value * 8}]" : src)}");
                    break;
                }

                case Func:
                    calls++;
                    Builder.AppendLine(op + ":");
                    Builder.AppendLine($"pop qword [Register3+{calls * 8}]");
                    break;

                case Ret:
                    Builder.AppendLine($"push qword [Register3+{calls * 8}]");
                    Builder.AppendLine("ret");
                    break;

                case Call:
                    Builder.AppendLine("call " + op);
                    break;

                case Add:
                    if (op is null && src is null)
                    {
                        Builder.AppendLine("pop rcx"); // Value 2
                        Builder.AppendLine("pop rdx"); // Value 1
                        Builder.AppendLine("add rdx,rcx");
                    }
                    else if (op is not null && src is null)
                    {
                        Builder.AppendLine("pop rdx"); // Value 1
                        Builder.AppendLine("add rdx," + op);
                    }
                    Builder.AppendLine("push rdx");
                    break;

                case And:
                    if (op is null && src is null)
                    {
                        Builder.AppendLine("pop rcx"); // Value 2
                        Builder.AppendLine("pop rdx"); // Value 1
                        Builder.AppendLine("and rdx,rcx");
                    }
                    else if (op is not null && src is null)
                    {
                        Builder.AppendLine("pop rdx"); // Value 1
                        Builder.AppendLine("and rdx," + op);
                    }
                    Builder.AppendLine("push rdx");
                    break;

                case Sub:
                    if (op is null && src is null)
                    {
                        Builder.AppendLine("pop rcx"); // Value 2
                        Builder.AppendLine("pop rdx"); // Value 1
                        Builder.AppendLine("sub rdx,rcx");
                    }
                    else if (op is not null && src is null)
                    {
                        Builder.AppendLine("pop rdx"); // Value 1
                        Builder.AppendLine("sub rdx," + op);
                    }
                    Builder.AppendLine("push rdx");
                    break;

                case Mul:
                    Builder.AppendLine("pop rcx"); // Value 2
                    Builder.AppendLine("pop rdx"); // Value 1
                    Builder.AppendLine("imul rdx,rcx");
                    Builder.AppendLine("push rdx");
                    break;

                case Div:
                    Builder.AppendLine("xor rdx,rdx");
                    Builder.AppendLine("pop rcx"); // Value 2
                    Builder.AppendLine("pop rax"); // Value 1
                    Builder.AppendLine("idiv rcx");
                    Builder.AppendLine("push rax");
                    break;

                case Or:
                    if (op is null && src is null)
                    {
                        Builder.AppendLine("pop rcx"); // Value 2
                        Builder.AppendLine("pop rdx"); // Value 1
                        Builder.AppendLine("or rdx,rcx");
                    }
                    else if (op is not null && src is null)
                    {
                        Builder.AppendLine("pop rdx"); // Value 1
                        Builder.AppendLine("or rdx," + op);
                    }
                    Builder.AppendLine("push rdx");
                    break;

                case Xor:
                    if (op is null && src is null)
                    {
                        Builder.AppendLine("pop rcx"); // Value 2
                        Builder.AppendLine("pop rdx"); // Value 1
                        Builder.AppendLine("xor rdx,rcx");
                    }
                    else if (op is not null && src is null)
                    {
                        Builder.AppendLine("pop rdx"); // Value 1
                        Builder.AppendLine("xor rdx," + op);
                    }
                    Builder.AppendLine("push rdx");
                    break;

                case Shl:
                    if (op is null && src is null)
                    {
                        Builder.AppendLine("pop rcx"); // Value 2
                        Builder.AppendLine("pop rdx"); // Value 1
                        Builder.AppendLine("shl rdx,cl");
                    }
                    else if (op is not null && src is null)
                    {
                        Builder.AppendLine("pop rdx"); // Value 1
                        Builder.AppendLine("shl rdx," + op);
                    }
                    Builder.AppendLine("push rdx");
                    break;

                case Shr:
                    if (op is null && src is null)
                    {
                        Builder.AppendLine("pop rcx"); // Value 2
                        Builder.AppendLine("pop rdx"); // Value 1
                        Builder.AppendLine("shr rdx,cl");
                    }
                    else if (op is not null && src is null)
                    {
                        Builder.AppendLine("pop rdx"); // Value 1
                        Builder.AppendLine("shr rdx," + op);
                    }
                    Builder.AppendLine("push rdx");
                    break;

                case Memstore8:
                    Builder.AppendLine("pop rcx"); // Value
                    Builder.AppendLine("pop rdx"); // Address
                    Builder.AppendLine("mov byte [rdx],cl");
                    break;

                case Memload8:
                    Builder.AppendLine("xor rcx,rcx");
                    Builder.AppendLine("pop rdx"); // Address
                    Builder.AppendLine("mov cl,byte [rdx]");
                    Builder.AppendLine("push rcx");
                    break;

                case Memstore16:
                    Builder.AppendLine("pop rcx"); // Value
                    Builder.AppendLine("pop rdx"); // Address
                    Builder.AppendLine("mov word [rdx],cx");
                    break;

                case Memload16:
                    Builder.AppendLine("xor rcx,rcx");
                    Builder.AppendLine("pop rdx"); // Address
                    Builder.AppendLine("mov cx,word [rdx]");
                    Builder.AppendLine("push rcx");
                    break;

                case Memstore32:
                    Builder.AppendLine("pop rcx"); // Value
                    Builder.AppendLine("pop rdx"); // Address
                    Builder.AppendLine("mov dword [rdx],ecx");
                    break;

                case Memload32:
                    Builder.AppendLine("xor rcx,rcx");
                    Builder.AppendLine("pop rdx"); // Address
                    Builder.AppendLine("mov ecx,dword [rdx]");
                    Builder.AppendLine("push rcx");
                    break;

                case Memstore64:
                    Builder.AppendLine("pop rcx"); // Value
                    Builder.AppendLine("pop rdx"); // Address
                    Builder.AppendLine("mov qword [rdx],rcx");
                    break;

                case Memload64:
                    Builder.AppendLine("pop rdx"); // Address
                    Builder.AppendLine("mov rcx,qword [rdx]");
                    Builder.AppendLine("push rcx");
                    break;
                
                case Iostore8:
                    Builder.AppendLine("pop rax"); // Value
                    Builder.AppendLine("pop rdx"); // Port
                    Builder.AppendLine("out dx,al");
                    break;
                
                case Ioload8:
                    Builder.AppendLine("xor rax,rax");
                    Builder.AppendLine("pop rdx"); // Port
                    Builder.AppendLine("in al,dx");
                    Builder.AppendLine("push rax");
                    break;
                
                case Iostore16:
                    Builder.AppendLine("pop rax"); // Value
                    Builder.AppendLine("pop rdx"); // Port
                    Builder.AppendLine("out dx,ax");
                    break;
                
                case Ioload16:
                    Builder.AppendLine("xor rax,rax");
                    Builder.AppendLine("pop rdx"); // Port
                    Builder.AppendLine("in ax,dx");
                    Builder.AppendLine("push rax");
                    break;
                
                case Iostore32:
                    Builder.AppendLine("pop rax"); // Value
                    Builder.AppendLine("pop rdx"); // Port
                    Builder.AppendLine("out dx,eax");
                    break;
                
                case Ioload32:
                    Builder.AppendLine("xor rax,rax");
                    Builder.AppendLine("pop rdx"); // Port
                    Builder.AppendLine("in eax,dx");
                    Builder.AppendLine("push rax");
                    break;
                
                case Iostore64:
                    Builder.AppendLine("pop rax"); // Value
                    Builder.AppendLine("pop rdx"); // Port
                    Builder.AppendLine("out dx,rax");
                    break;
                
                case Ioload64:
                    Builder.AppendLine("pop rdx"); // Port
                    Builder.AppendLine("in rax,dx");
                    Builder.AppendLine("push rax");
                    break;
            }
        }

        File.WriteAllText(_asmPath, Builder.ToString());
        Utils.StartSilent("yasm", $"-fbin {_asmPath} -o {_binPath}");
    }

    public override void Link()
    {
        OutputStream = ELF.Link64(_binPath);
    }
}