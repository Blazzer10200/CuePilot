#!/usr/bin/env bash
# Compact client for CuePilot's focus-safe WebView2 inspection bridge.
set -euo pipefail

API="${CUEPILOT_CDP_API:-http://127.0.0.1:9323}"
SCRIPT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
UI_ROOT="$(cd -- "$SCRIPT_DIR/../.." && pwd)"

command -v curl >/dev/null 2>&1 || { echo "cuepilot-cdp requires curl" >&2; exit 3; }
command -v jq >/dev/null 2>&1 || { echo "cuepilot-cdp requires jq" >&2; exit 3; }

get() {
  local endpoint="$1" output
  output="$(curl -sS --max-time 30 "$API/$endpoint" 2>/dev/null)" || true
  if [ -z "$output" ]; then
    echo "cuepilot-cdp: no response from $API (run: npm run cdp:serve)" >&2
    return 7
  fi
  printf '%s' "$output"
}

post() {
  local endpoint="$1" body="$2" output
  output="$(curl -sS --max-time 120 -X POST "$API/$endpoint" -H 'Content-Type: application/json' --data "$body" 2>/dev/null)" || true
  if [ -z "$output" ]; then
    echo "cuepilot-cdp: no response from $API/$endpoint (run: npm run cdp:serve)" >&2
    return 7
  fi
  printf '%s' "$output"
}

render_error() {
  jq -r 'if .error then "[error] " + (.error|tostring), (if .hint then "  " + .hint else empty end), (if (.suggestions // [] | length) > 0 then "  suggestions:", (.suggestions[] | "    " + .selector + "  ←  " + (.label // "")) else empty end) else empty end'
}

render_look() {
  jq -r '
    if .page.error then
      "[look] ERROR: " + (.page.error|tostring)
    else
      "[look] " + (.page.title // "?")
        + " · " + (.page.location // "?")
        + " · " + ((.page.viewport.width // 0)|tostring) + "×" + ((.page.viewport.height // 0)|tostring)
        + (if .page.dialog then " · dialog=" + (.page.dialog|tostring) else "" end)
        + (if ((.page.scroll.maxY // 0) > 0) then " · scroll=" + ((.page.scroll.y // 0)|tostring) + "/" + ((.page.scroll.maxY // 0)|tostring) else " · fits" end),
      "[errors] " + ((.errorCount // 0)|tostring)
        + (if ((.staleErrors // 0) > 0) then " (+" + (.staleErrors|tostring) + " stale hidden)" else "" end),
      (.errors[]? | "  ✗ " + ((.text // "?")|tostring|.[0:320])),
      (if .screenshot then
        if .screenshot.error then "[shot] ERROR: " + (.screenshot.error|tostring)
        else .screenshot.path end
      else empty end)
    end'
}

render_action() {
  jq -r '
    .results as $results |
    ($results[0] // {}) as $action |
    ($results[1] // {}) as $settled |
    if $action.error then
      "[act] ERROR: " + ($action.error|tostring),
      (if (($action.suggestions // [])|length) > 0 then "  suggestions:", ($action.suggestions[] | "    " + .selector + "  ←  " + (.label // "")) else empty end)
    else
      "[act] OK" + (if $action.via then " via=" + $action.via else "" end)
        + (if $action.chars then " chars=" + ($action.chars|tostring) else "" end)
    end,
    (if $settled.error then "[settled] ERROR: " + ($settled.error|tostring)
     else "[settled] " + (($settled.waitedMs // 0)|tostring) + "ms"
       + (if $settled.quiet == false then " (DOM still changing)" else " quiet" end)
       + " · " + (($settled.mutations // 0)|tostring) + " mutation(s)" end)'
}

cmd="${1:-}"
shift || true

case "$cmd" in
  health|targets)
    get "$cmd" | jq
    ;;

  doctor)
    echo "[doctor] CuePilot CDP"
    if wrapper="$(curl -sS --max-time 3 "$API/health" 2>/dev/null)" && [ -n "$wrapper" ]; then
      if [ "$(printf '%s' "$wrapper" | jq -r '.ok // false')" = "true" ]; then
        printf '%s' "$wrapper" | jq -r '"  ✓ wrapper :9323 · WebView2 :9322 · " + (.target.title // "?") + " · " + ((.pingMs // 0)|tostring) + "ms"'
        exit 0
      fi
      echo "  ✓ wrapper :9323 is running"
      printf '%s' "$wrapper" | jq -r '"  ✗ WebView2 target unavailable: " + (.error // "unknown error")'
    else
      echo "  ✗ wrapper :9323 is not running"
    fi

    if curl -sS --max-time 3 "http://127.0.0.1:9322/json/version" >/dev/null 2>&1; then
      echo "  ✓ WebView2 CDP :9322 is available"
      echo "  → start the wrapper: cd \"$UI_ROOT\" && npm run cdp:serve"
    else
      echo "  ✗ WebView2 CDP :9322 is not available"
      echo "  → relaunch the dev app: cd \"$UI_ROOT\" && npm run cdp:dev"
      echo "  → then start the wrapper: npm run cdp:serve"
    fi
    ;;

  page)
    get page | jq '.value // .'
    ;;

  errors)
    limit="${1:-30}"
    all="${2:-}"
    suffix="?level=error&limit=$limit"
    [ "$all" = "--all" ] && suffix="$suffix&all=true"
    get "console$suffix" | jq -r '
      "[errors] " + (.count|tostring) + " current"
        + (if (.stale // 0) > 0 then " · " + (.stale|tostring) + " stale hidden" else "" end),
      (.logs[]? | "  ✗ [" + (.kind // "?") + "/g" + ((.generation // 0)|tostring) + "] " + ((.text // "?")|tostring|.[0:360]))'
    ;;

  inspect)
    selector="${1:-}"
    limit="${2:-120}"
    look_params="$(jq -nc --arg selector "$selector" '{noShot:true} + (if $selector == "" then {} else {selector:$selector} end)')"
    ax_params="$(jq -nc --arg selector "$selector" --argjson limit "$limit" '{limit:$limit} + (if $selector == "" then {} else {selector:$selector} end)')"
    body="$(jq -nc --argjson look "$look_params" --argjson ax "$ax_params" '{parallel:true,operations:[{op:"look",params:$look},{op:"ax",params:$ax}]}')"
    response="$(post batch "$body")"
    printf '%s' "$response" | jq -r '
      "[inspect] state + errors + accessibility · " + ((.elapsedMs // 0)|tostring) + "ms · no screenshot",
      (.results[0] | if .page.error then "[page] ERROR: " + (.page.error|tostring)
       else "[page] " + (.page.title // "?") + " · " + (.page.location // "?")
         + " · " + ((.page.viewport.width // 0)|tostring) + "×" + ((.page.viewport.height // 0)|tostring)
         + (if ((.page.scroll.maxY // 0) > 0) then " · scrollable=" + ((.page.scroll.maxY // 0)|tostring) + "px" else " · fits" end)
         + (if .page.dialog then " · dialog=" + (.page.dialog|tostring) else "" end),
         "[errors] " + ((.errorCount // 0)|tostring),
         (.errors[]? | "  ✗ " + ((.text // "?")|tostring|.[0:320])) end),
      (.results[1] | if .error then "[ax] ERROR: " + (.error|tostring)
       else "[ax] " + (.count|tostring) + "/" + (.total|tostring) + " node(s)"
         + (if .truncated then " (capped)" else "" end),
         (.nodes[]? | "  " + .role + ": " + (.name // "")
           + (if .value != null then " = " + (.value|tostring) else "" end)
           + (if .state then " [" + .state + "]" else "" end)) end)'
    ;;

  map)
    selector="${1:-}"
    limit="${2:-100}"
    hidden="${3:-}"
    body="$(jq -nc --arg selector "$selector" --argjson limit "$limit" --arg hidden "$hidden" '{limit:$limit,includeHidden:($hidden=="--all")} + (if $selector == "" then {} else {selector:$selector} end)')"
    response="$(post controls "$body")"
    printf '%s' "$response" | jq -r '
      if .error then "[map] ERROR: " + (.error|tostring),
        (if ((.suggestions // [])|length) > 0 then "  suggestions:", (.suggestions[] | "    " + .selector + "  ←  " + (.label // "")) else empty end)
      else "[map] " + ((.controls|length)|tostring) + "/" + (.total|tostring) + " actionable control(s)"
        + (if .truncated then " (capped)" else "" end),
        (.controls[]? | "  " + .selector + "  ←  " + .role + " \"" + ((.label // "")|.[0:72]) + "\""
          + (if .state then " [" + .state + "]" else "" end)
          + (if .visible then "" else " [HIDDEN]" end)
          + " @" + (.rect.x|tostring) + "," + (.rect.y|tostring) + " " + (.rect.width|tostring) + "×" + (.rect.height|tostring))
      end'
    ;;

  find)
    query="${1:-}"
    limit="${2:-15}"
    [ -n "$query" ] || { echo "usage: $0 find <text> [limit]" >&2; exit 2; }
    response="$(post find "$(jq -nc --arg query "$query" --argjson limit "$limit" '{query:$query,limit:$limit}')")"
    printf '%s' "$response" | jq -r --arg query "$query" '
      if .error then "[find] ERROR: " + (.error|tostring)
      else "[find] " + (.count|tostring) + " match(es) for \"" + $query + "\"",
        (.matches[]? | "  " + .selector + "  ←  " + .tag + (if .role then "[" + .role + "]" else "" end)
          + " \"" + ((.label // "")|.[0:90]) + "\""
          + (if .visible then "" else " [HIDDEN]" end)
          + (if .disabled then " [disabled]" else "" end)) end'
    ;;

  text)
    selector="${1:-}"
    max_chars="${2:-5000}"
    body="$(jq -nc --arg selector "$selector" --argjson max "$max_chars" '{maxChars:$max} + (if $selector == "" then {} else {selector:$selector} end)')"
    response="$(post text "$body")"
    printf '%s' "$response" | jq -r '
      if .error then "[text] ERROR: " + (.error|tostring)
      else "[text] " + (.totalChars|tostring) + " chars" + (if .truncated then " (truncated)" else "" end), "---", .text end'
    ;;

  measure)
    selector="${1:-}"
    mode="${2:-}"
    [ -n "$selector" ] || { echo "usage: $0 measure <selector> [nokids]" >&2; exit 2; }
    body="$(jq -nc --arg selector "$selector" --arg mode "$mode" '{selector:$selector,children:($mode!="nokids")}')"
    post measure "$body" | jq
    ;;

  ax)
    selector="${1:-}"
    limit="${2:-120}"
    full="${3:-}"
    body="$(jq -nc --arg selector "$selector" --argjson limit "$limit" --arg full "$full" '{limit:$limit,full:($full=="full")} + (if $selector == "" then {} else {selector:$selector} end)')"
    post ax "$body" | jq
    ;;

  look|peek)
    selector="${1:-}"
    no_shot=false
    [ "$cmd" = "peek" ] && no_shot=true
    body="$(jq -nc --arg selector "$selector" --argjson noShot "$no_shot" '{noShot:$noShot} + (if $selector == "" then {} else {selector:$selector} end)')"
    post look "$body" | render_look
    ;;

  act)
    action="${1:-}"
    shift || true
    look_selector=""
    settle_ms="1600"
    case "$action" in
      click)
        selector="${1:-}"; look_selector="${2:-}"; settle_ms="${3:-1600}"
        [ -n "$selector" ] || { echo "usage: $0 act click <selector> [look-selector] [max-settle-ms]" >&2; exit 2; }
        operation="$(jq -nc --arg selector "$selector" '{op:"click",params:{selector:$selector}}')"
        ;;
      key)
        key="${1:-}"; look_selector="${2:-}"; settle_ms="${3:-1600}"
        [ -n "$key" ] || { echo "usage: $0 act key <key-or-combo> [look-selector] [max-settle-ms]" >&2; exit 2; }
        operation="$(jq -nc --arg key "$key" '{op:"key",params:{key:$key}}')"
        ;;
      type)
        selector="${1:-}"; text="${2:-}"; key="${3:-}"; look_selector="${4:-}"; settle_ms="${5:-1600}"
        [ -n "$selector" ] || { echo "usage: $0 act type <selector> <text> [key] [look-selector] [max-settle-ms]" >&2; exit 2; }
        operation="$(jq -nc --arg selector "$selector" --arg text "$text" --arg key "$key" '{op:"type",params:{selector:$selector,text:$text} + (if $key=="" then {} else {key:$key} end)}')"
        ;;
      *)
        echo "usage: $0 act {click|key|type} ..." >&2
        exit 2
        ;;
    esac
    look_params="$(jq -nc --arg selector "$look_selector" 'if $selector=="" then {} else {selector:$selector} end')"
    body="$(jq -nc --argjson operation "$operation" --argjson settle "$settle_ms" --argjson look "$look_params" '{operations:[$operation,{op:"settle",params:{quietMs:140,maxMs:$settle}},{op:"look",params:$look}]}')"
    response="$(post batch "$body")"
    printf '%s' "$response" | render_action
    printf '%s' "$response" | jq -c '.results[2]' | render_look
    ;;

  eval)
    expression="${1:-}"
    [ -n "$expression" ] || { echo "usage: $0 eval <javascript>" >&2; exit 2; }
    post eval "$(jq -nc --arg expression "$expression" '{expression:$expression}')" | jq
    ;;

  click)
    selector="${1:-}"
    [ -n "$selector" ] || { echo "usage: $0 click <selector>" >&2; exit 2; }
    post click "$(jq -nc --arg selector "$selector" '{selector:$selector}')" | jq
    ;;

  key)
    key="${1:-}"
    [ -n "$key" ] || { echo "usage: $0 key <key-or-combo>" >&2; exit 2; }
    post key "$(jq -nc --arg key "$key" '{key:$key}')" | jq
    ;;

  type)
    selector="${1:-}"; text="${2:-}"; key="${3:-}"
    [ -n "$selector" ] || { echo "usage: $0 type <selector> <text> [key]" >&2; exit 2; }
    post type "$(jq -nc --arg selector "$selector" --arg text "$text" --arg key "$key" '{selector:$selector,text:$text} + (if $key=="" then {} else {key:$key} end)')" | jq
    ;;

  wait)
    expression="${1:-}"; timeout="${2:-30000}"
    [ -n "$expression" ] || { echo "usage: $0 wait <javascript-condition> [timeout-ms]" >&2; exit 2; }
    post wait "$(jq -nc --arg expression "$expression" --argjson timeout "$timeout" '{expression:$expression,timeoutMs:$timeout}')" | jq
    ;;

  ready)
    post wait '{"expression":"document.readyState === \"complete\" && Boolean(document.querySelector(\"main\"))","timeoutMs":60000}' | jq
    ;;

  shot)
    format="${1:-jpeg}"; quality="${2:-78}"
    post screenshot "$(jq -nc --arg format "$format" --argjson quality "$quality" '{format:$format,quality:$quality}')" | jq -r 'if .error then "[shot] ERROR: " + (.error|tostring) else .path end'
    ;;

  shot-sel)
    selector="${1:-}"; format="${2:-jpeg}"; quality="${3:-78}"
    [ -n "$selector" ] || { echo "usage: $0 shot-sel <selector> [format] [quality]" >&2; exit 2; }
    post screenshot "$(jq -nc --arg selector "$selector" --arg format "$format" --argjson quality "$quality" '{selector:$selector,format:$format,quality:$quality}')" | jq -r 'if .error then "[shot] ERROR: " + (.error|tostring) else .path end'
    ;;

  reload)
    post reload '{}' | jq
    ;;

  shutdown)
    post shutdown '{}' | jq
    ;;

  *)
    cat >&2 <<'USAGE'
usage: c.sh <command>

  doctor                         diagnose launcher/wrapper availability
  health | targets | page        inspect the bridge and current page
  inspect [selector] [limit]     state + errors + accessibility, no screenshot
  map [selector] [limit]         list visible controls with actionable selectors
  find <text> [limit]            find controls/headings by rendered label
  text [selector] [max-chars]    read exact rendered text
  measure <selector> [nokids]    computed geometry and styling
  look [selector]                state + errors + screenshot
  peek [selector]                state + errors without screenshot
  act click <selector>           click + settle + verified look
  act key <key-or-combo>         key + settle + verified look
  act type <selector> <text>     type + settle + verified look
  errors [limit] [--all]         current-generation console errors
  eval <javascript>              evaluate JavaScript in the WebView
  shot | shot-sel <selector>     write a screenshot under scripts/cdp/.tmp
  ready | reload | shutdown      lifecycle helpers
USAGE
    exit 2
    ;;
esac
