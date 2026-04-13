---
title: "Custom patterns"
description: "Reusable pattern-matching logic with the pattern keyword"
order: 13
---

The `pattern` keyword allows for the encapsulation of matching logic in reusable components.

A custom pattern is ultimately a function with a signature but no name; instead the pattern is identified by the tag(s) it returns. Custom patterns return tags that use the type naming scheme (to distinguish them from standard tags), meaning they must start with a capital letter after the `#`.

The function body contains the logic for matching and extracting values, and returns one of the tags used in the pattern signature. As with tags elsewhere in the language, members of the pattern union can carry payloads. This gives the custom pattern the ability to return values that are then available in the match body.

# Total patterns

A pattern is _total_ if it matches all possible inputs.

A total pattern can have one choice:

```nanyx
pattern Rgb -> #Hsl(h: float, s: float, l: float) = { rgb ->
  -- conversion logic here
  def h = ...
  def s = ...
  def l = ...
  
  #Hsl(h, s, l)
}

match getRgbColor()
  | #Hsl(h, s, l) -> println("Turned color into HSL: {h}, {s}, {l}")

-- since the pattern is single-choice and total, we can even use it directly in a value binding:

def #Hsl(h, s, l) = getRgbColor()
```

or many:

```nanyx
pattern int -> #Even | #Odd = { n -> if n % 2 == 0 then #Even else #Odd }
```

# Partial patterns

Custom patterns can be partial (meaning they might not match all possible inputs). For a pattern to be partial, one of its return values must be a wildcard (`_`).

As a convenience, Nanyx allows a partial pattern with exactly one choice and no output valuess to return a `bool` instead of an explicit tag:

```nanyx
pattern int -> #Zero | _:  = { n -> n == 0 }

match num
  | #Zero -> "zero"
  | _ -> "non-zero"

-- Alternatively, we can use if-matching style:
if #Zero = num then
  println("It's zero!")
else
  println("It's not zero.")
```

In the case of partial patterns with multiple choices or output values, the wildcard case must be explicitly returned using the `_` value:

```nanyx
pattern string -> #ParseInt(int) | #ParseBool(bool) | _ = { s ->
  if | #some(i) = int.parse(s) -> #ParseInt(i)
     | #some(b) = bool.parse(s) -> #ParseBool(b)
     | else -> _
}

match getUserInput()
  | #ParseInt(i) -> println("Parsed an integer: {i}")
  | #ParseBool(b) -> println("Parsed a boolean: {b}")
  | _ -> println("Input is neither an integer nor a boolean.")
```

# Patterns with arguments

Patterns can also take arguments, which are values that are passed in when the pattern is used. This allows for more flexible and reusable patterns and are useful for complex conditions like regex matching:

```nanyx
pattern Regex -> string -> #MatchesRegex | _:  = { s, r ->
  s \Regex.matches(r)
}

match userInput
  | #MatchesRegex("[^@]+@\w+\.\w+") -> "valid email"
  | _ -> "invalid format"
```

A pattern with arguments has the form `<args> -> <input> -> <output>`, where the first part is the argument type, the second part is the input type that the pattern matches against, and the third part is the output tag union indicating match success or failure.

```nanyx
-- A pattern that checks whether a string contains a given character and splits on it
pattern char -> string -> #SplitOnChar(string, string) | _ = { c, s ->
  def parts = s.split(c)
  if parts.length == 2 then #SplitOnChar(parts[0], parts[1]) else #_
}

match "hello,world"
  | #SplitOnChar(',')(s1, s2) -> "First: {s1}, Second: {s2}"
  | _ -> "Input string does not contain a comma"
```

You can also compose patterns to handle more sophisticated matching:

```nanyx
pattern int -> #PositiveInt | _ = { > 0 }

match value
  | #PositiveInt & { < 1000 } -> "small positive"
  | #PositiveInt -> "large positive"
  | _ -> "non-positive"
```
