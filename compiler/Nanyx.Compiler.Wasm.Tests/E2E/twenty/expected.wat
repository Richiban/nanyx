(module
  (memory 1)
  (export "memory" (memory 0))

  (type $ctx_fn_1 (func (param i32) (result i32)))
  (table $ctx_table 2 funcref)
  (elem (i32.const 0) $__ctx_main_ToString_int_toString_0 $__ctx_main_ToString_string_toString_1)

  (data (i32.const 256) "\04\00\00\00fake\00")

  (func $compareStrings (param $x i32) (param $y i32) (param $__ctx_ToString_a_toString i32) (param $__ctx_ToString_b_toString i32) (result i32)
    local.get $x
    local.get $__ctx_ToString_a_toString
    call_indirect (type $ctx_fn_1)
    local.get $y
    local.get $__ctx_ToString_b_toString
    call_indirect (type $ctx_fn_1)
    i32.eq
  )
  (export "compareStrings" (func $compareStrings))

  (func $main (result i32)
    i32.const 42
    i32.const 260
    i32.const 0
    i32.const 1
    call $compareStrings
  )
  (export "main" (func $main))

  (func $__ctx_main_ToString_int_toString_0 (param $i i32) (result i32)
    i32.const 260
  )

  (func $__ctx_main_ToString_string_toString_1 (param $s i32) (result i32)
    local.get $s
  )
)
