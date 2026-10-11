class_name ScalarMembers
extends RefCounted

# Count starts at three.
var Count := 3

# Value assigned later.
var PendingA : float
# Typed collection assigned later.
var IntegerValues : Array[int]
var TextValue : String

# Ratio has a multiline note.
# It is intentionally generic.
var Ratio := 1.5

var Label := "sample"

func scale(value: float) -> float:
    return value * Ratio

# Shared by all instances.
static var SharedCount := 4
static var SharedLabel : String

static func resetShared():
    SharedCount = 0
