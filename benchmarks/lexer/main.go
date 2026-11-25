package main

import (
	"fmt"
	"os"
	"strconv"
)

type AtomType int

const (
	AtomKeyword AtomType = iota
	AtomIdentifier
	AtomLiteralInt
	AtomLiteralBool
	AtomMarker
	AtomEOF
)

type Atom struct {
	Type    AtomType
	Text    string
	IntVal  int64
	BoolVal bool
}

type Particle struct {
	Line   int
	Column int
	Len    int
	Atom   Atom
}

var keywords = map[string]bool{
	"val": true, "var": true, "fn": true, "return": true, "if": true, "then": true,
	"else": true, "match": true, "when": true, "yield": true, "for": true, "in": true,
	"break": true, "continue": true, "struct": true, "enum": true, "union": true,
	"interface": true, "pub": true, "is": true, "static": true, "module": true,
	"from": true, "import": true,
}

var markerMap = map[byte]string{
	'(': "LPAREN", ')': "RPAREN",
	'[': "LBRACKET", ']': "RBRACKET",
	'{': "LBRACE", '}': "RBRACE",
	':': "COLON", ',': "COMMA",
}

func isIdentChar(c byte) bool {
	return (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || c == '_'
}

func isNumChar(c byte) bool {
	return c >= '0' && c <= '9'
}

func lexProgram(program string) []Particle {
	atomList := make([]Particle, 0, len(program)/6)
	lineNum := 1
	column := 1
	identifierLen := 0
	numericalLen := 0
	totalLen := len(program)

	pushIdentifier := func(i, length int) {
		begin := i - length
		sub := program[begin:i]
		var atom Atom
		if len(sub) <= 10 && keywords[sub] {
			atom = Atom{Type: AtomKeyword, Text: sub}
		} else if sub == "true" || sub == "false" {
			atom = Atom{Type: AtomLiteralBool, BoolVal: sub == "true"}
		} else {
			atom = Atom{Type: AtomIdentifier, Text: sub}
		}
		atomList = append(atomList, Particle{
			Line:   lineNum,
			Column: column - length,
			Len:    length,
			Atom:   atom,
		})
	}

	pushNumerical := func(i, length int) {
		begin := i - length
		sub := program[begin:i]
		n, _ := strconv.ParseInt(sub, 10, 64)
		atomList = append(atomList, Particle{
			Line:   lineNum,
			Column: column - length,
			Len:    length,
			Atom:   Atom{Type: AtomLiteralInt, IntVal: n},
		})
	}

	for i := 0; i < totalLen; i++ {
		c := program[i]

		if isIdentChar(c) {
			identifierLen++
			column++
			continue
		} else if identifierLen > 0 {
			pushIdentifier(i, identifierLen)
			identifierLen = 0
		}

		if isNumChar(c) {
			numericalLen++
			column++
			continue
		} else if numericalLen > 0 {
			pushNumerical(i, numericalLen)
			numericalLen = 0
		}

		if c == '\n' {
			lineNum++
			column = 1
			continue
		}

		if c == ' ' || c == '\t' {
			column++
			continue
		}

		if markerName, ok := markerMap[c]; ok {
			atomList = append(atomList, Particle{
				Line:   lineNum,
				Column: column,
				Len:    1,
				Atom:   Atom{Type: AtomMarker, Text: markerName},
			})
			column++
			continue
		}

		column++
	}

	if identifierLen > 0 {
		pushIdentifier(totalLen, identifierLen)
	} else if numericalLen > 0 {
		pushNumerical(totalLen, numericalLen)
	}

	atomList = append(atomList, Particle{
		Line:   lineNum,
		Column: column,
		Len:    1,
		Atom:   Atom{Type: AtomEOF},
	})

	return atomList
}

func main() {
	bytes, err := os.ReadFile("benchmarks/data/lexer.txt")
	if err != nil {
		fmt.Fprintf(os.Stderr, "Error reading file: %v\n", err)
		os.Exit(1)
	}
	content := string(bytes)

	particles := lexProgram(content)

	kwCount := 0
	identCount := 0
	var intSum int64 = 0
	boolTrue := 0
	markerCount := 0

	for _, p := range particles {
		switch p.Atom.Type {
		case AtomKeyword:
			kwCount++
		case AtomIdentifier:
			identCount++
		case AtomLiteralInt:
			intSum = (intSum + p.Atom.IntVal) % 1000000007
		case AtomLiteralBool:
			if p.Atom.BoolVal {
				boolTrue++
			}
		case AtomMarker:
			markerCount++
		}
	}

	fmt.Println(len(particles))
	fmt.Println(kwCount)
	fmt.Println(identCount)
	fmt.Println(intSum)
	fmt.Println(boolTrue)
	fmt.Println(markerCount)
}
