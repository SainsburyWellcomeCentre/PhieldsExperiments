# HelvarNet — Controlling Lights over TCP/IP

Notes and reference for talking to a Helvar lighting router directly using the
**HelvarNet** ASCII protocol. The goal is a Bonsai workflow that can discover
devices on the router and fade lights in/out as part of an experiment.

> Source: recovered early experiments against the router at `10.254.1.4`,
> cross-checked against the official [HelvarNet Overview spec](https://aca.im/driver_docs/Helvar/HelvarNet-Overview.pdf)
> and community implementations. HelvarNet is Helvar's official third-party
> interface to the 910/920 router system — the same family Helvar Designer uses
> to manage the workgroup and control fixtures.

---

## 1. Connection

| | |
|---|---|
| Transport | TCP, port **50000** (fixed — the router's listener port cannot be changed) |
| Router IP | `10.254.1.4` |
| Encoding | ASCII text, one command per message (max 1500 bytes) |

The router IP itself encodes part of the lighting hierarchy:

```
10 . 254 . <cluster> . <router>
10 . 254 .    1       .    4
```

So `10.254.1.4` is **cluster 1, router 4**. _(spec: the `10.254.x.y` scheme is the
standard Helvar router addressing convention.)_

**Important consequence:** even though the TCP connection is already pointed at
cluster 1 / router 4, commands still have to repeat the cluster and router in
their address field. The connection target and the address parameter are not
linked — the address is always absolute.

---

## 2. Addressing hierarchy

Devices are addressed top-down through four levels:

```
@ cluster . router . subnet . device
@   1     .   4    .   3    .   32
```

| Level   | Example | Meaning |
|---------|---------|---------|
| cluster | `1`     | matches 3rd octet of router IP |
| router  | `4`     | matches 4th octet of router IP |
| subnet  | `3`     | one of the router's DALI subnets |
| device  | `32`    | device index within the subnet (from a discovery query) |

A partial address addresses everything below it, e.g. `@0` = cluster 0,
`@1.4.3` = every device on subnet 3 of router 4 in cluster 1.

---

## 3. Message format

```
> V:<ver> , C:<cmd> [ , <param>:<value> ... ] , @<address> #
│ └──────┬───────┘                              └────┬───┘ │
│     parameters (comma-separated key:value)       address │
│                                                          │
└─ ">" = command/request prefix          message terminator ┘
```

- `>` starts a command; **`#` is the message terminator** — it is the actual
  end-of-message marker the router keys off, and it is already part of every
  command string. No CR/LF is required (PuTTY appends one on Enter, but the
  router ignores trailing bytes until the next `>`). _(`?` is a query, `!` a
  reply, `<` an internal command.)_
- Parameters are `Key:Value`, comma-separated, in any order.
- The `@` address is the last field before `#`.

### Parameter reference

| Key | Name | Notes |
|-----|------|-------|
| `V` | Protocol version | `1` or `2`. Query commands (100/101/102) use `V:2`; level commands use `V:1` in the recovered notes. |
| `C` | Command number | see table below |
| `L` | Level | light level, **0–100 %** |
| `F` | Fade time | **centiseconds** (1/100 s), range 0–65535 (= 0–6553.5 s). `F:100` = 1 s, `F:9000` = 90 s |
| `@` | Address | `cluster.router.subnet.device` (may be partial). Ranges: cluster 1–253, router 1–254, subnet 1–4, device 1–255 |

### Command reference (seen in the notes)

| `C` | Command | Used with |
|-----|---------|-----------|
| `11`  | **Recall Scene** — recall a stored scene on a group/block | `G` (group), `B` (block), `S` (scene), `K` (constant light), `F` |
| `14`  | **Direct Level Device** — set a device to a level over a fade time | `V:1`, `L`, `F`, `@…device` |
| `100` | **Query Device Types & Addresses** — discover devices on a subnet | `V:2`, `@…subnet` |
| `101` | **Query Clusters** | `V:2` |
| `102` | **Query Routers** (on a cluster) | `V:2`, `@cluster` |

Example Recall Scene (group 1, block 1, scene 5, 90 s fade):
`>V:2,C:11,G:1,K:1,B:1,S:5,F:9000#`

---

## 4. Discovery flow

Walk the hierarchy from the top to find addressable devices:

```text
1. Query clusters
   > V:2,C:101#

2. Query routers on cluster 0
   > V:2,C:102,@0#

3. Query device types & addresses on subnet 3 of router 4 / cluster 1
   > V:2,C:100,@1.4.3#
   → reply lists each device as  <type>@<index>   e.g.  1507@1
        1507 = device type code,  1 = device index within the subnet
```

The device index from step 3 is what you plug into the 4th address slot to
control a specific fitting.

---

## 5. Controlling a light (worked example)

Once you know a device address — here cluster 1, router 4, subnet 3, device 32:

```text
Fade UP to 100% over 1 second:
   > V:1,C:14,L:100,F:100,@1.4.3.32#

Fade DOWN to 0% over 1 second:
   > V:1,C:14,L:0,F:100,@1.4.3.32#

Slow fade to 60% over 90 seconds:
   > V:1,C:14,L:60,F:9000,@1.4.3.32#
```

---

## 6. Raw command log (recovered, verbatim)

```text
10.254.1.4

(Query clusters, V2)
>V:2,C:101#

(Query routers on cluster 0, V2)
>V:2,C:102,@0#

>V:1,C:14,L:60,F:9000,@3.32#
>V:1,C:14,L:0,F:100,@1.3.32#

(Query devices on subnet 3 of router 4 / cluster 1)
>V:2,C:100,@1.4.3#

(Fade device 32 in / out)
>V:1,C:14,L:100,F:100,@1.4.3.32#
>V:1,C:14,L:0,F:100,@1.4.3.32#
```

---

## 7. Bonsai implementation plan (next step)

**Why a custom C# operator:** Bonsai's only built-in TCP support is in the
`Bonsai.Osc` package (`CreateTcpClient`/`SendMessage`), which wraps everything
in Open Sound Control framing (OSC address patterns + a 4-byte TCP length
prefix). It cannot emit a clean raw `>…#` ASCII string, so it is unsuitable for
HelvarNet. We therefore add a small custom operator under `Extensions/` — the
same pattern this repo already uses in `grant_jan_2025`.

The demo workflow will:

1. Open a **TCP client** to `10.254.1.4:50000`.
2. Send a `Direct Level Device` string (`C:14`) built from experiment
   parameters (level, fade, address); the `#` already terminates it.
3. Read back replies (split on `#`) for query commands during device discovery.

Resolved design points:

- **Port** — TCP 50000, fixed. ✓
- **Terminator** — the protocol terminator is `#`, already in the command
  string. The client exposes a `Terminator` property for any *extra* trailing
  bytes, **defaulting to empty** — set it to `\r`/`\r\n` only if a specific
  router build turns out to need it.
```
