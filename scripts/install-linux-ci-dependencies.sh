#!/usr/bin/env bash
set -Eeuo pipefail

# GitHub-hosted Ubuntu runners can occasionally lose contact with an apt
# mirror. Keep that transient problem from occupying a runner indefinitely.
readonly apt_command_timeout_seconds=120
readonly apt_attempts=2
readonly apt_retry_delay_seconds=5

run_apt() {
    local operation="$1"
    shift

    local attempt
    local exit_code
    for ((attempt = 1; attempt <= apt_attempts; attempt++)); do
        echo "apt-get $operation (Versuch $attempt/$apt_attempts) ..."
        if sudo -n timeout --foreground --kill-after=15s "${apt_command_timeout_seconds}s" \
            apt-get \
            -o Acquire::Retries=3 \
            -o Acquire::http::Timeout=30 \
            -o Acquire::https::Timeout=30 \
            -o DPkg::Lock::Timeout=60 \
            -o Dpkg::Use-Pty=0 \
            "$@"; then
            return 0
        fi
        exit_code=$?

        if ((attempt == apt_attempts)); then
            echo "apt-get $operation ist nach $apt_attempts Versuchen fehlgeschlagen (Exitcode $exit_code)." >&2
            return "$exit_code"
        fi

        echo "apt-get $operation fehlgeschlagen (Exitcode $exit_code), neuer Versuch in ${apt_retry_delay_seconds}s ..." >&2
        sleep "$apt_retry_delay_seconds"
    done
}

run_apt update update
run_apt install install --yes --no-install-recommends \
    bzip2 \
    libegl1 \
    libgbm1 \
    libgl1 \
    libgl1-mesa-dri \
    libinput10 \
    openssh-server \
    xvfb
