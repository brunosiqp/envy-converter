#!/usr/bin/env bash
# Teste no emulador: instala, abre um PDF, converte para DOCX e confere o resultado.
set -euo pipefail
PKG=com.brunosiqueira.envyconverter
OUT=dist
mkdir -p "$OUT"

shot() { adb exec-out screencap -p > "$OUT/$1.png" || true; }

tap_text() {
  adb shell uiautomator dump /sdcard/ui.xml >/dev/null
  local bounds
  bounds=$(adb shell cat /sdcard/ui.xml | grep -o "text=\"$1\"[^>]*bounds=\"[^\"]*\"" | head -1 | grep -o 'bounds="[^"]*"' | grep -oE '[0-9]+' | tr '\n' ' ')
  [ -n "$bounds" ] || { echo "Botão '$1' não encontrado"; return 1; }
  read -r x1 y1 x2 y2 <<< "$bounds"
  adb shell input tap $(( (x1 + x2) / 2 )) $(( (y1 + y2) / 2 ))
}

# Alertas "app não está respondendo" do emulador lento atrapalham os toques.
dismiss() {
  for t in Wait Aguardar "Close app"; do tap_text "$t" 2>/dev/null && sleep 2 || true; done
}

adb shell settings put global hide_error_dialogs 1 || true
adb install -r EnvyConverter.apk
adb push amostra.pdf /data/local/tmp/amostra.pdf
adb shell chmod 644 /data/local/tmp/amostra.pdf

ACTIVITY=$(adb shell cmd package resolve-activity --brief "$PKG" | tail -1 | tr -d '\r')
adb shell am start -W -a android.intent.action.VIEW -d file:///data/local/tmp/amostra.pdf -t application/pdf -n "$ACTIVITY"
sleep 25
shot 1-abertura

adb shell pidof "$PKG" >/dev/null || { echo "::error::O app fechou ao abrir"; adb logcat -d | grep -iE "fatal|exception|crash" | tail -40; exit 1; }

dismiss
if tap_text "Converter"; then
  sleep 30
  dismiss
  shot 2-convertido
  adb shell ls -la /sdcard/Download/EnvyConverter/ | tee "$OUT/downloads.txt" || true
  if adb shell ls /sdcard/Download/EnvyConverter/ | grep -q "amostra.docx"; then
    adb pull /sdcard/Download/EnvyConverter/amostra.docx "$OUT/amostra.docx"
    unzip -tq "$OUT/amostra.docx"
    echo "Conversão no emulador OK."
  else
    echo "::warning::DOCX não encontrado em Downloads (veja as capturas)."
  fi
else
  echo "::warning::Não foi possível tocar em Converter (o PDF de exemplo não chegou ao app?)."
fi
adb logcat -d | grep -iE "AndroidRuntime|FATAL|mono-rt" | tail -40 > "$OUT/logcat.txt" || true
