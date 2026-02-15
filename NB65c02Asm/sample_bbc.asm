; BBC Micro 6502 example (MOS)
; Prints "HELLO WORLD" using OSWRCH at &FFEE.
; Note: This is for BBC Micro; it will not run on C64.

.include "os_constants.asm"

.org $1900

start:
    LDA #13
    JSR OSWRCH

    LDA #'H'
    JSR OSWRCH
    LDA #'E'
    JSR OSWRCH
    LDA #'L'
    JSR OSWRCH
    LDA #'L'
    JSR OSWRCH
    LDA #'O'
    JSR OSWRCH
    LDA #' '
    JSR OSWRCH
    LDA #'W'
    JSR OSWRCH
    LDA #'O'
    JSR OSWRCH
    LDA #'R'
    JSR OSWRCH
    LDA #'L'
    JSR OSWRCH
    LDA #'D'
    JSR OSWRCH

    LDA #13
    JSR OSWRCH

    RTS
