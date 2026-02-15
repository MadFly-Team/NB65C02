; C64 PRG entry at $0801
; Prints "HELLO WORLD" by calling KERNAL CHROUT ($FFD2).
.org $0801

start:
    LDA #$0D        ; CR
    JSR $FFD2

    LDA #'H'
    JSR $FFD2
    LDA #'E'
    JSR $FFD2
    LDA #'L'
    JSR $FFD2
    LDA #'L'
    JSR $FFD2
    LDA #'O'
    JSR $FFD2
    LDA #' '
    JSR $FFD2
    LDA #'W'
    JSR $FFD2
    LDA #'O'
    JSR $FFD2
    LDA #'R'
    JSR $FFD2
    LDA #'L'
    JSR $FFD2
    LDA #'D'
    JSR $FFD2

    LDA #$0D        ; CR
    JSR $FFD2

    RTS
