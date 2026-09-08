#!/usr/bin/env bash
# Single-process supervisor for the Notesnook sync stack.
# One systemd unit (notesnook.service) execs this; it launches Identity + API
# (+ optional SSE) as children in the same cgroup. KillMode=mixed in the unit
# means "systemctl stop notesnook" SIGTERMs this script, which tears the
# children down, then systemd SIGKILLs anything left in the cgroup.
set -u

APP_ROOT="${APP_ROOT:-/opt/notesnook}"
RUN_SSE="${RUN_SSE:-0}"

# .env is the single source of truth (DotNetEnv also reads it). Sourced here only
# so this script knows the health-check ports. Must be plain KEY=value.
set -a
# shellcheck disable=SC1091
[ -f "$APP_ROOT/.env" ] && . "$APP_ROOT/.env"
set +a

ID_PORT="${IDENTITY_SERVER_PORT:-8264}"
API_PORT="${NOTESNOOK_SERVER_PORT:-5264}"
SSE_PORT="${SSE_SERVER_PORT:-7264}"

export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1
export ASPNETCORE_ENVIRONMENT=Production
# Keep any stray DataProtection/tmp writes inside a writable path.
export HOME="$APP_ROOT/data"

pids=()
stop() {
	trap - TERM INT
	kill -TERM "${pids[@]}" 2>/dev/null
	wait
	exit 0
}
trap stop TERM INT

start() { # label subdir dll
	( cd "$APP_ROOT/$2" && exec dotnet "$3" ) &
	pids+=("$!")
	echo "notesnook: started $1 (pid $!)"
}

wait_health() { # label url
	for _ in $(seq 1 90); do
		[ "$(curl -fsS -o /dev/null -m 3 -w '%{http_code}' "$2" 2>/dev/null)" = "200" ] && {
			echo "notesnook: $1 healthy"
			return 0
		}
		sleep 2
	done
	echo "notesnook: $1 did NOT become healthy at $2" >&2
	return 1
}

start identity identity Streetwriters.Identity.dll
wait_health identity "http://127.0.0.1:${ID_PORT}/health" || stop

start api api Notesnook.API.dll
wait_health api "http://127.0.0.1:${API_PORT}/health" || stop

if [ "$RUN_SSE" = "1" ]; then
	start sse sse Streetwriters.Messenger.dll
	wait_health sse "http://127.0.0.1:${SSE_PORT}/health" || echo "notesnook: sse unhealthy (continuing)"
fi

# Exit as soon as any child dies so systemd (Restart=on-failure) recycles the unit.
wait -n
rc=$?
echo "notesnook: a child exited (rc=$rc) — stopping the rest" >&2
stop
