(module
  (func $getOrDefault (param $opt i32) (param $default i32) (result i32) (local $__match_tmp_0 i32) (local $__payload_value i32)
    local.get $opt
    local.set $__match_tmp_0
    local.get $__match_tmp_0
      i32.const -65536
      i32.and
      i32.const 131072
      i32.eq
    (if (result i32)
      (then
        local.get $__match_tmp_0
      i32.const 65535
      i32.and
        local.set $__payload_value
    local.get $__payload_value
      )
      (else
        local.get $__match_tmp_0
      i32.const -65536
      i32.and
      i32.const 65536
      i32.eq
    (if (result i32)
      (then
        local.get $default
      )
      (else
        unreachable
      )
    )
      )
    )
  )
  (export "getOrDefault" (func $getOrDefault))

  (func $main (result i32) (local $x i32) (local $y i32) (local $a i32) (local $b i32)
    i32.const 42
    i32.const 65535
    i32.and
    i32.const 131072
    i32.or
    local.set $x
    i32.const 65536
    local.set $y
    local.get $x
    i32.const 0
    call $getOrDefault
    local.set $a
    local.get $y
    i32.const 99
    call $getOrDefault
    local.set $b
    local.get $a
    local.get $b
    i32.add
  )
  (export "main" (func $main))
)