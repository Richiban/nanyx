(module
  (import "wasi_snapshot_preview1" "fd_write" (func $fd_write (param i32 i32 i32 i32) (result i32)))
  (memory 1)
  (export "memory" (memory 0))

  (global $heap_ptr (mut i32) (i32.const 4096))

  (func $__nanyx_alloc (param $size i32) (result i32)
    (local $ptr i32)
    global.get $heap_ptr
    local.set $ptr
    local.get $ptr
    local.get $size
    i32.add
    global.set $heap_ptr
    block $fits
      loop $grow
        global.get $heap_ptr
        memory.size
        i32.const 65536
        i32.mul
        i32.le_u
        br_if $fits
        i32.const 1
        memory.grow
        i32.const -1
        i32.eq
        if
          unreachable
        end
        br $grow
      end
    end
    local.get $ptr
  )
  (func $__nanyx_string_length (param $s i32) (result i32)
    local.get $s
    i32.const 4
    i32.sub
    i32.load
  )
  (func $__nanyx_string_alloc (param $len i32) (result i32)
    (local $base i32)
    local.get $len
    i32.const 5
    i32.add
    call $__nanyx_alloc
    local.set $base
    local.get $base
    local.get $len
    i32.store
    local.get $base
    i32.const 4
    i32.add
    local.get $len
    i32.add
    i32.const 0
    i32.store8
    local.get $base
    i32.const 4
    i32.add
  )
  (func $__nanyx_string_from_byte (param $byte i32) (result i32)
    (local $out i32)
    i32.const 1
    call $__nanyx_string_alloc
    local.set $out
    local.get $out
    local.get $byte
    i32.store8
    local.get $out
  )
  (func $__nanyx_write_stdout (param $s i32)
    i32.const 0
    local.get $s
    i32.store
    i32.const 4
    local.get $s
    call $__nanyx_string_length
    i32.store
    i32.const 1
    i32.const 0
    i32.const 1
    i32.const 8
    call $fd_write
    drop
  )

  (data (i32.const 256) "\0d\00\00\00Hello, World!\00")

  (func $print (param $text i32) (result i32)
    local.get $text
    call $__nanyx_write_stdout
    i32.const 0
  )
  (export "print" (func $print))

  (func $println (param $text i32) (result i32)
    local.get $text
    call $__nanyx_write_stdout
    i32.const 0
    drop
    i32.const 10
    call $__nanyx_string_from_byte
    call $__nanyx_write_stdout
    i32.const 0
  )
  (export "println" (func $println))

  (func $main (result i32)
    i32.const 260
    call $println
  )
  (export "main" (func $main))

  (func $__nanyx_start
    call $main
    drop
  )

  (start $__nanyx_start)
)
