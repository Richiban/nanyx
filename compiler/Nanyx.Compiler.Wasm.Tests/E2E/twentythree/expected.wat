(module
  (import "wasi_snapshot_preview1" "fd_write" (func $fd_write (param i32 i32 i32 i32) (result i32)))
  (memory 1)
  (export "memory" (memory 0))
  (data (i32.const 224) "\0a")
  (func $println_str (param $ptr i32)
    i32.const 0
    local.get $ptr
    i32.store
    i32.const 4
    local.get $ptr
    i32.const 4
    i32.sub
    i32.load
    i32.store
    i32.const 1
    i32.const 0
    i32.const 1
    i32.const 8
    call $fd_write
    drop
    i32.const 0
    i32.const 224
    i32.store
    i32.const 4
    i32.const 1
    i32.store
    i32.const 1
    i32.const 0
    i32.const 1
    i32.const 8
    call $fd_write
    drop
  )

  (data (i32.const 256) "\0d\00\00\00Hello, Nanyx!\00")

  (func $__nanyx_start
    i32.const 260
    call $println_str
    i32.const 0
    drop
  )

  (start $__nanyx_start)
)