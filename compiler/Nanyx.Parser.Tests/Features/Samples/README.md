# Nyx Parser Test Data

This directory contains all test files used by the parser tests and for manual testing.

## File Categories

### Original Test Files (from initial test suite)
- `binary-operators.nyx` - Binary operator parsing tests
- `comprehensive.nyx` - Comprehensive language feature tests
- `function-calls.nyx` - Function call syntax tests
- `lambdas.nyx` - Lambda expression tests
- `literals.nyx` - Literal value tests
- `multiline-functions.nyx` - Multi-line function definition tests
- `piping.nyx` - Pipe operator tests
- `real-world.nyx` - Real-world usage examples
- `shorthand-lambdas.nyx` - Shorthand lambda syntax tests

### Sample Files
- `sample*.nyx` - Various sample code files for manual testing

### Match Expression Tests (Phases 1-5)
- `test_match_basic.nyx` - Basic match expressions
- `test_match_simple.nyx` - Simple match patterns
- `test_match_binding.nyx` - Pattern binding tests
- `test_match_lambda.nyx` - Match with lambda expressions
- `test_match_tuple.nyx` - Tuple pattern tests (Phase 3)
- `test_match_record.nyx` - Record pattern tests (Phase 3)
- `test_match_mixed.nyx` - Mixed pattern tests (Phase 3)
- `test_match_list_basic.nyx` - Basic list patterns (Phase 4)
- `test_match_list_splat.nyx` - List patterns with trailing splats (Phase 4)
- `test_match_list_middle.nyx` - List patterns with middle splats (Phase 4)
- `test_match_range.nyx` - Range pattern tests (Phase 5)
- `test_match_guard.nyx` - Guard pattern tests (Phase 5)
- `test_match_tag.nyx` - Tag pattern tests (Phase 5)
- `test_match_tag_patterns.nyx` - Additional tag pattern tests (Phase 5)

### Phase 5 Comprehensive Tests
- `test_phase5_three.nyx` - All three Phase 5 pattern types together
- `test_phase5_complete.nyx` - Comprehensive Phase 5 test
- `test_range_copy.nyx` - Range pattern basic test
- `test_simple_range.nyx` - Simple range test
- `test_simple_range_expr.nyx` - Range expression test
- `test_tag_simple.nyx` - Simple tag pattern test
- `test_tag_simple2.nyx` - Another simple tag pattern test
- `test_just_range_in_match.nyx` - Range in match expression

### Lambda Tests
- `test_lambda_identity.nyx` - Identity lambda test
- `test_lambda_func_call.nyx` - Lambda with function calls
- `test_lambda_no_params_func_call.nyx` - Parameterless lambda tests
- `test_trailing_lambda.nyx` - Trailing lambda syntax

### Multiline Tests
- `test_multiline_simple.nyx` - Simple multiline test
- `test_multiline_two.nyx` - Two-line test
- `test_multiline_func.nyx` - Multiline function test
- `test_multiline_ident.nyx` - Multiline with identifiers
- `test_multiline_with_params.nyx` - Multiline with parameters
- `test_multiline_multi_params.nyx` - Multiline with multiple parameters

### Miscellaneous Tests
- `test_simple_func_call.nyx` - Simple function call test
- `test_simple_literals.nyx` - Simple literal pattern test
- `test_multidef.nyx` - Multiple definitions test
- `test_single.nyx` - Single definition test
- `test_debug.nyx` - Debug test file

### Debug Files
- `test_just_range.txt` - Raw range pattern for debugging (parsed as pattern, not module)

## Usage

### In Tests
Tests automatically load files from this directory using:
```fsharp
let testDataDir = Path.Combine(__SOURCE_DIRECTORY__, "testdata")
let filePath = Path.Combine(testDataDir, filename)
```

### Manual Testing
From the `compiler` directory:
```bash
dotnet run --project Nanyx.Compiler.Cli/NanyxCompiler.Cli.csproj -- parse Nanyx.Parser.Tests/Features/Samples/<filename>.nyx
```
- Files ending in `.nyx` are parsed as complete modules
