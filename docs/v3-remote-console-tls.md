# V3 Remote Console — TLS (optional)

The v3 remote console protocol runs over plain TCP by default. When the
console is reachable from an untrusted network (shared VPN, public Wi-Fi,
any setting where someone else can sniff the loopback link), turn TLS on
to wrap the socket in `SslStream`.

## Enabling TLS

### Server (engine)

In `appsettings.json` (or the live `settings.json` if it already exists):

```json
{
  "remote_console_enabled": true,
  "remote_console_port": 9000,
  "remote_console_tls_enabled": true
}
```

On the next start, the engine generates a self-signed certificate at
`%AppData%\ProSoft\EasySave\cert.pfx` (Windows) — the equivalent
`~/.config/ProSoft/EasySave/cert.pfx` on Linux / `~/Library/...` on macOS.
The file is a password-less PFX; filesystem ACLs are the only protection.

### Client (RemoteConsole)

Open `EasySave.RemoteConsole`, tick the **TLS** checkbox next to the
host / port fields before clicking **Connect**.

## Trust on first use

The client does not validate the server certificate against a CA chain
(self-signed certs have none). Instead it uses a TOFU policy modelled on
OpenSSH's `known_hosts`:

* On the **first** connection to a given `host:port`, the cert thumbprint
  is pinned in `%AppData%\ProSoft\EasySave.RemoteConsole\known_hosts.txt`
  and the connection is accepted.
* On every **subsequent** connection, the cert thumbprint must still match.
  A different cert means either the server was reissued or a man-in-the-
  middle is talking back — the connection is refused.

## Replacing the self-signed cert with a real CA-issued one

The self-signed cert is fine for a dev / school setup. To plug in a real
cert (e.g. one issued by your organisation's PKI):

1. Stop the engine.
2. Drop the new `cert.pfx` at the path shown above, **same filename**,
   password-less (export with `openssl pkcs12 -export -out cert.pfx
   -inkey key.pem -in cert.pem -passout pass:`).
3. Restart the engine.
4. On every client, **delete the entry** for that `host:port` from
   `known_hosts.txt` so the new cert can pin on the next connection.

## Operational notes

* `cert.pfx` lifetime is 5 years. Past that, regenerate (delete the file
  and let the engine recreate it) and wipe the matching client
  `known_hosts.txt` entries.
* TLS 1.2 and TLS 1.3 are enabled; older protocols are off.
* Certificate revocation checks are disabled — useless for a self-signed
  cert, and a real cert in this deployment has no public OCSP responder.
* No client certificate is required by the server. Authentication of the
  console operator stays out of scope for v3.
