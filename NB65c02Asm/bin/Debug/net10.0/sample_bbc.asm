; BBC Micro 6502 example (MOS)
; Prints "HELLO WORLD" using OSWRCH at &FFEE.
; Note: This is for BBC Micro; it will not run on C64.
.org $1900

.include "os_defines.asm"

start:
    LDA #13
    JSR OSWRITE

    LDA #'H'
    JSR $FFEE
    LDA #'E'
    JSR $FFEE
    LDA #'L'
    JSR $FFEE
    LDA #'L'
    JSR $FFEE
    LDA #'O'
    JSR $FFEE
    LDA #' '
    JSR $FFEE
    LDA #'W'
    JSR $FFEE
    LDA #'O'
    JSR $FFEE
    LDA #'R'
    JSR $FFEE
    LDA #'L'
    JSR $FFEE
    LDA #'D'
    JSR $FFEE

    LDA #13
    JSR $FFEE

    RTS
