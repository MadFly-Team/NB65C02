; -----------------------------------------------------
; File:   Tester.asm
; Coder:  Neil Beresford
; -----------------------------------------------------

.output "build/tester-new.ssd"

; BBC Micro 6502 example (MOS)
; Prints "HELLO WORLD" using OSWRCH at &FFEE.
; Note: This is for BBC Micro; it will not run on C64.

.org $1900

; The following are included via the project file, if not using please add.

;	JMP start		; Skip the included source...
;.include "65C02src/os_constants.asm"
;.include "invader1.asm"

start:

    ; VDU 22 — select screen mode
    LDA #22
	JSR OSWRITE

    ; Mode number
    LDA #128				; BBC Master mode... for shadow memory
	JSR OSWRITE
	; draw invader sprite
	LDA #12
	STA SPRITEX
	LDA #2
	STA SPRITEY
	JSR draw_Invader1

TestMessage:

	LDX #0
.loop:
	LDA .txtMessage,X
	CMP #0
	BEQ .end
	JSR OSWRITE
	INX
	JMP .loop
.end:
	RTS

.txtMessage:

	.byte 13,10,10
	.text "Hello World!"
	.byte 13,10,10,0

; -----------------------------------------------------
; End of file:   tester.asm
; -----------------------------------------------------