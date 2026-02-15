; ─── Sprite: Invader1 ───
; Target: MODE 2  Size: 16×8
; Colours: 16 logical colours

; ─── Sprite: Sprite1 ───
; Target: MODE 0  Size: 8×8
; Colours: 2 logical colours

Sprite1_W = 8
Sprite1_H = 8

Sprite1_chars:
    .byte $20, $20, $20, $20, $20, $20, $20, $20
    .byte $20, $20, $23, $23, $23, $23, $20, $20
    .byte $23, $23, $23, $23, $23, $23, $23, $23
    .byte $23, $23, $20, $23, $20, $23, $20, $23
    .byte $23, $23, $23, $23, $23, $23, $23, $20
    .byte $23, $20, $20, $20, $20, $20, $23, $20
    .byte $20, $23, $20, $20, $20, $23, $20, $20
    .byte $20, $20, $20, $20, $20, $20, $20, $20

Sprite1_fg:
    .byte $00, $00, $00, $00, $00, $00, $00, $00
    .byte $00, $00, $01, $01, $01, $01, $00, $00
    .byte $01, $01, $01, $01, $01, $01, $01, $01
    .byte $01, $01, $00, $01, $00, $01, $00, $01
    .byte $01, $01, $01, $01, $01, $01, $01, $00
    .byte $01, $00, $00, $00, $00, $00, $01, $00
    .byte $00, $01, $00, $00, $00, $01, $00, $00
    .byte $00, $00, $00, $00, $00, $00, $00, $00

Sprite1_bg:
    .byte $00, $00, $00, $00, $00, $00, $00, $00
    .byte $00, $00, $00, $00, $00, $00, $00, $00
    .byte $00, $00, $00, $00, $00, $00, $00, $00
    .byte $00, $00, $00, $00, $00, $00, $00, $00
    .byte $00, $00, $00, $00, $00, $00, $00, $00
    .byte $00, $00, $00, $00, $00, $00, $00, $00
    .byte $00, $00, $00, $00, $00, $00, $00, $00
    .byte $00, $00, $00, $00, $00, $00, $00, $00

; Draw sprite at screen position ($70) = column, ($71) = row
draw_Invader1:
    LDX #0              ; byte index into tables
    LDY #0              ; current row offset
.draw_Sprite1_row:

    ; VDU 31,col,row — move text cursor
    LDA #31
    JSR $FFEE           ; OSWRCH
    CLC
    TYA                 ; row offset
    ADC $71            ; + base row
    PHA                 ; save row for OSWRCH
    LDA $70            ; base column
    JSR $FFEE           ; OSWRCH — X coord
    PLA
    JSR $FFEE           ; OSWRCH — Y coord

    STX $70+2          ; save X (use 72 as temp)
    LDA #0
    STA $70+3          ; column counter
.draw_Sprite1_col:
    LDA #17
    JSR $FFEE           ; OSWRCH — VDU 17
    LDA Sprite1_fg,X
    JSR $FFEE           ; OSWRCH — foreground colour
    LDA #17
    JSR $FFEE           ; OSWRCH — VDU 17
    LDA Sprite1_bg,X
    ORA #$80            ; bit 7 = background
    JSR $FFEE           ; OSWRCH — background colour
    LDA Sprite1_chars,X
    JSR $FFEE           ; OSWRCH — print character

    INX
    INC $70+3          ; column++
    LDA $70+3
    CMP #8
    BNE .draw_Sprite1_col

    INY                 ; next row
    CPY #8
    BNE .draw_Sprite1_row

    LDA #17
    JSR $FFEE           ; OSWRCH — VDU 17
    LDA #1
    JSR $FFEE           ; OSWRCH — foreground colour
    LDA #17
    JSR $FFEE           ; OSWRCH — VDU 17
    LDA #0
    ORA #$80            ; bit 7 = background
    JSR $FFEE           ; OSWRCH — background colour


    RTS