using System;
using System.Collections.Generic;
using System.Globalization;

namespace ForcedUpscaling
{
    // Texts in four languages. Order in every row: en, pt, zh, es.
    static class Loc
    {
        public static readonly string[] Codes = { "en", "pt", "zh", "es" };
        public static readonly string[] Names = { "English", "Português", "中文", "Español" };

        static int index;

        static readonly Dictionary<string, string[]> table = new Dictionary<string, string[]>
        {
            { "open", new[] { "Open", "Abrir", "打开", "Abrir" } },
            { "resolution", new[] { "Resolution: {0}×{1} @ {2} Hz", "Resolução: {0}×{1} a {2} Hz", "分辨率：{0}×{1} @ {2} Hz", "Resolución: {0}×{1} a {2} Hz" } },
            { "enable", new[] { "Turn on Forced Upscaling", "Ativar Forced Upscaling", "开启 Forced Upscaling", "Activar Forced Upscaling" } },
            { "disable", new[] { "Forced Upscaling is on (click to turn off)", "Forced Upscaling ativo (clique para desativar)", "Forced Upscaling 已开启（点击关闭）", "Forced Upscaling activo (clic para desactivar)" } },
            { "monitor", new[] { "Monitor", "Monitor", "显示器", "Monitor" } },
            { "primary", new[] { "primary", "principal", "主显示器", "principal" } },
            { "res", new[] { "Resolution", "Resolução", "分辨率", "Resolución" } },
            { "scale", new[] { "Windows scale", "Escala do Windows", "Windows 缩放比例", "Escala de Windows" } },
            { "scale_auto", new[] { "Automatic", "Automática", "自动", "Automática" } },
            { "recommended", new[] { "recommended", "recomendado", "推荐", "recomendado" } },
            { "game", new[] { "Game", "Jogo", "游戏", "Juego" } },
            { "game_all", new[] { "All games", "Todos os jogos", "所有游戏", "Todos los juegos" } },
            { "dof", new[] { "Depth of field", "Profundidade de campo", "景深", "Profundidad de campo" } },
            { "layers", new[] { "Internal resolution", "Resolução interna", "内部分辨率", "Resolución interna" } },
            { "layers_full", new[] { "Full resolution", "Resolução total", "全分辨率", "Resolución completa" } },
            { "layers_game", new[] { "Game default", "Padrão do jogo", "游戏默认", "Predeterminado del juego" } },
            { "dof_normal", new[] { "Normal", "Normal", "正常", "Normal" } },
            { "dof_weak", new[] { "Weak", "Fraco", "弱", "Débil" } },
            { "dof_off", new[] { "Off", "Desligado", "关闭", "Desactivado" } },
            { "autostart", new[] { "Start with Windows", "Iniciar com o Windows", "开机自动启动", "Iniciar con Windows" } },
            { "support", new[] { "Support", "Incentivar", "支持", "Apoyar" } },
            { "by", new[] { "By: iLinzer", "Por: iLinzer", "作者：iLinzer", "Por: iLinzer" } },
            { "exit", new[] { "Exit", "Encerrar", "退出", "Salir" } },
            { "language", new[] { "Language", "Idioma", "语言", "Idioma" } },
            { "on", new[] { "On", "Ativo", "已开启", "Activo" } },
            { "off", new[] { "Off", "Desativado", "已关闭", "Desactivado" } },
            { "minimize", new[] { "Minimize", "Minimizar", "最小化", "Minimizar" } },
            { "close", new[] { "Close", "Fechar", "关闭", "Cerrar" } },
            { "no_modes", new[] { "No DSR resolutions available", "Nenhuma resolução DSR disponível", "没有可用的 DSR 分辨率", "No hay resoluciones DSR disponibles" } },
            { "no_monitor", new[] { "Main monitor not found", "Monitor principal não encontrado", "未找到主显示器", "No se encontró el monitor principal" } },
            { "tip_on", new[] { "Forced Upscaling: on ({0}×{1})", "Forced Upscaling: ativo ({0}×{1})", "Forced Upscaling：已开启（{0}×{1}）", "Forced Upscaling: activo ({0}×{1})" } },
            { "tip_off", new[] { "Forced Upscaling: off", "Forced Upscaling: desativado", "Forced Upscaling：已关闭", "Forced Upscaling: desactivado" } },
            { "on_title", new[] { "Forced Upscaling is on", "Forced Upscaling ativo", "Forced Upscaling 已开启", "Forced Upscaling activo" } },
            { "on_text", new[] {
                "{0}×{1} on the main monitor. Open your game and turn it off here when you are done.",
                "{0}×{1} no monitor principal. Abra o jogo e, ao terminar, desative aqui.",
                "主显示器已切换到 {0}×{1}。请启动游戏，用完后在这里关闭。",
                "{0}×{1} en el monitor principal. Abre el juego y desactívalo aquí al terminar." } },
            { "off_title", new[] { "Forced Upscaling is off", "Forced Upscaling desativado", "Forced Upscaling 已关闭", "Forced Upscaling desactivado" } },
            { "off_text", new[] { "The main monitor is back to {0}×{1}.", "O monitor principal voltou a {0}×{1}.", "主显示器已恢复为 {0}×{1}。", "El monitor principal vuelve a {0}×{1}." } },
            { "fail_title", new[] { "Could not turn it on", "Não foi possível ativar", "无法开启", "No se pudo activar" } },
            { "fail", new[] {
                "The driver refused {0}×{1} (code {2}). In the NVIDIA Control Panel open Manage 3D settings > DSR - Factors, clear them all, apply, then tick them again.",
                "O driver recusou {0}×{1} (código {2}). No Painel da NVIDIA, abra Gerenciar configurações em 3D > DSR - Fatores, desmarque tudo, aplique e marque de novo.",
                "驱动程序拒绝了 {0}×{1}（代码 {2}）。请在 NVIDIA 控制面板的“管理 3D 设置”中找到“DSR - 系数”，全部取消勾选并应用，然后重新勾选。",
                "El controlador rechazó {0}×{1} (código {2}). En el Panel de control de NVIDIA abre Administrar la configuración 3D > DSR - Factores, desmárcalos todos, aplica y vuelve a marcarlos." } }
        };

        public static string Current
        {
            get { return Codes[index]; }
        }

        public static string FontName
        {
            get { return Current == "zh" ? "Microsoft YaHei UI" : "Segoe UI"; }
        }

        public static void Set(string code)
        {
            int i = Array.IndexOf(Codes, code);
            index = i < 0 ? 0 : i;
        }

        // first run: follow the Windows display language, English if we don't know it
        public static string Detect()
        {
            string two = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.ToLowerInvariant();
            return Array.IndexOf(Codes, two) >= 0 ? two : "en";
        }

        public static string T(string key)
        {
            string[] row;
            return table.TryGetValue(key, out row) ? row[index] : key;
        }

        public static string F(string key, params object[] args)
        {
            return string.Format(CultureInfo.CurrentCulture, T(key), args);
        }
    }
}
