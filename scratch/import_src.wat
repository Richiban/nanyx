(module
  (import "wasi_snapshot_preview1" "fd_write" (func $fd_write (param i32 i32 i32 i32) (result i32)))
  (memory 1)
  (export "memory" (memory 0))
  (data (i32.const 128) "dbg: ")
  (data (i32.const 224) "\0a")
  (func $dbg (param $x i32)
    (local $n i32)
    (local $isneg i32)
    (local $pos i32)
    (local $start i32)
    (local $lo i32)
    (local $hi i32)
    (local $tmp i32)
    local.get $x
    local.set $n
    local.get $n
    i32.const 0
    i32.lt_s
    if
      i32.const 1
      local.set $isneg
      local.get $n
      i32.const -1
      i32.mul
      local.set $n
    end
    i32.const 192
    local.set $pos
    local.get $isneg
    if
      local.get $pos
      i32.const 45
      i32.store8
      local.get $pos
      i32.const 1
      i32.add
      local.set $pos
    end
    local.get $n
    i32.const 0
    i32.eq
    if
      local.get $pos
      i32.const 48
      i32.store8
      local.get $pos
      i32.const 1
      i32.add
      local.set $pos
    else
      local.get $pos
      local.set $start
      loop $digits
        local.get $pos
        local.get $n
        i32.const 10
        i32.rem_u
        i32.const 48
        i32.add
        i32.store8
        local.get $pos
        i32.const 1
        i32.add
        local.set $pos
        local.get $n
        i32.const 10
        i32.div_u
        local.set $n
        local.get $n
        i32.const 0
        i32.ne
        br_if $digits
      end
      local.get $start
      local.set $lo
      local.get $pos
      i32.const 1
      i32.sub
      local.set $hi
      block $rev_done
        loop $rev
          local.get $lo
          local.get $hi
          i32.ge_u
          br_if $rev_done
          local.get $lo
          i32.load8_u
          local.set $tmp
          local.get $lo
          local.get $hi
          i32.load8_u
          i32.store8
          local.get $hi
          local.get $tmp
          i32.store8
          local.get $lo
          i32.const 1
          i32.add
          local.set $lo
          local.get $hi
          i32.const 1
          i32.sub
          local.set $hi
          br $rev
        end
      end
    end
    local.get $pos
    i32.const 10
    i32.store8
    local.get $pos
    i32.const 1
    i32.add
    local.set $pos
    i32.const 0
    i32.const 128
    i32.store
    i32.const 4
    local.get $pos
    i32.const 128
    i32.sub
    i32.store
    i32.const 1
    i32.const 0
    i32.const 1
    i32.const 8
    call $fd_write
    drop
  )
  (func $dbg_str (param $ptr i32)
    (local $len i32)
    local.get $ptr
    i32.const 4
    i32.sub
    i32.load
    local.set $len
    i32.const 0
    i32.const 128
    i32.store
    i32.const 4
    i32.const 5
    i32.store
    i32.const 1
    i32.const 0
    i32.const 1
    i32.const 8
    call $fd_write
    drop
    i32.const 0
    local.get $ptr
    i32.store
    i32.const 4
    local.get $len
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

  (global $heap_ptr (mut i32) (i32.const 4096))

  (func $child_double (param $x i32) (result i32)
    local.get $x
    i32.const 2
    i32.mul
  )
  (export "child.double" (func $child_double))

  (func $result (result i32)
    i32.const 21
    call $child_double
  )
  (export "result" (func $result))

  (func $__nanyx_start (local $__dbg_tmp i32)
    i32.const 0
    local.set $__interp_len
    local.get $__interp_len
    i32.const 8
    i32.add
    local.set $__interp_len
    call $result
    local.set $__interp_tmp
    local.get $__interp_tmp
    i32.const 0
    i32.lt_s
    if
      local.get $__interp_len
      i32.const 1
      i32.add
      local.set $__interp_len
      local.get $__interp_tmp
      i32.const -1
      i32.mul
      local.set $__interp_tmp
    end
    local.get $__interp_tmp
    i32.const 0
    i32.eq
    if
      local.get $__interp_len
      i32.const 1
      i32.add
      local.set $__interp_len
    else
      loop $interp_int_len
        local.get $__interp_len
        i32.const 1
        i32.add
        local.set $__interp_len
        local.get $__interp_tmp
        i32.const 10
        i32.div_u
        local.set $__interp_tmp
        local.get $__interp_tmp
        i32.const 0
        i32.ne
        br_if $interp_int_len
      end
    end
    global.get $heap_ptr
    local.tee $__interp_base
    local.get $__interp_len
    i32.store
    local.get $__interp_base
    i32.const 4
    i32.add
    local.set $__interp_cursor
    local.get $__interp_cursor
    i32.const 82
    i32.store8
    local.get $__interp_cursor
    i32.const 1
    i32.add
    local.set $__interp_cursor
    local.get $__interp_cursor
    i32.const 101
    i32.store8
    local.get $__interp_cursor
    i32.const 1
    i32.add
    local.set $__interp_cursor
    local.get $__interp_cursor
    i32.const 115
    i32.store8
    local.get $__interp_cursor
    i32.const 1
    i32.add
    local.set $__interp_cursor
    local.get $__interp_cursor
    i32.const 117
    i32.store8
    local.get $__interp_cursor
    i32.const 1
    i32.add
    local.set $__interp_cursor
    local.get $__interp_cursor
    i32.const 108
    i32.store8
    local.get $__interp_cursor
    i32.const 1
    i32.add
    local.set $__interp_cursor
    local.get $__interp_cursor
    i32.const 116
    i32.store8
    local.get $__interp_cursor
    i32.const 1
    i32.add
    local.set $__interp_cursor
    local.get $__interp_cursor
    i32.const 58
    i32.store8
    local.get $__interp_cursor
    i32.const 1
    i32.add
    local.set $__interp_cursor
    local.get $__interp_cursor
    i32.const 32
    i32.store8
    local.get $__interp_cursor
    i32.const 1
    i32.add
    local.set $__interp_cursor
    call $result
    local.set $__interp_tmp
    local.get $__interp_tmp
    i32.const 0
    i32.lt_s
    if
      local.get $__interp_cursor
      i32.const 45
      i32.store8
      local.get $__interp_cursor
      i32.const 1
      i32.add
      local.set $__interp_cursor
      local.get $__interp_tmp
      i32.const -1
      i32.mul
      local.set $__interp_tmp
    end
    local.get $__interp_tmp
    i32.const 0
    i32.eq
    if
      local.get $__interp_cursor
      i32.const 48
      i32.store8
      local.get $__interp_cursor
      i32.const 1
      i32.add
      local.set $__interp_cursor
    else
      local.get $__interp_cursor
      local.set $__interp_start
      loop $interp_digits
        local.get $__interp_cursor
        local.get $__interp_tmp
        i32.const 10
        i32.rem_u
        i32.const 48
        i32.add
        i32.store8
        local.get $__interp_cursor
        i32.const 1
        i32.add
        local.set $__interp_cursor
        local.get $__interp_tmp
        i32.const 10
        i32.div_u
        local.set $__interp_tmp
        local.get $__interp_tmp
        i32.const 0
        i32.ne
        br_if $interp_digits
      end
      local.get $__interp_start
      local.set $__interp_lo
      local.get $__interp_cursor
      i32.const 1
      i32.sub
      local.set $__interp_hi
      block $interp_rev_done
        loop $interp_rev
          local.get $__interp_lo
          local.get $__interp_hi
          i32.ge_u
          br_if $interp_rev_done
          local.get $__interp_lo
          i32.load8_u
          local.set $__interp_swap
          local.get $__interp_lo
          local.get $__interp_hi
          i32.load8_u
          i32.store8
          local.get $__interp_hi
          local.get $__interp_swap
          i32.store8
          local.get $__interp_lo
          i32.const 1
          i32.add
          local.set $__interp_lo
          local.get $__interp_hi
          i32.const 1
          i32.sub
          local.set $__interp_hi
          br $interp_rev
        end
      end
    end
    local.get $__interp_cursor
    i32.const 0
    i32.store8
    local.get $__interp_base
    local.get $__interp_len
    i32.const 5
    i32.add
    i32.add
    global.set $heap_ptr
    local.get $__interp_base
    i32.const 4
    i32.add
    local.tee $__dbg_tmp
    call $dbg_str
    local.get $__dbg_tmp
    drop
  )

  (start $__nanyx_start)
)