// AURA's small door into whisper.cpp: plain functions that are easy to call from C#.
// Built by the release workflow together with whisper.cpp into one DLL (aura_whisper.dll).
#include "whisper.h"
#include <stdlib.h>
#include <string.h>
#include <stdio.h>

#ifdef _WIN32
#define AW_API __declspec(dllexport)
#else
#define AW_API __attribute__((visibility("default")))
#endif

typedef void (*aw_progress)(int percent);
typedef int (*aw_stop)(void);

typedef struct {
    struct whisper_context * ctx;
    char * out;          // the result: one line per token, see aw_run
    size_t len, cap;
    aw_progress progress;
    aw_stop stop;
    char lang[16];
} aw;

static void put(aw * a, const char * s) {
    size_t n = strlen(s);
    if (a->len + n + 1 > a->cap) {
        size_t c = a->cap ? a->cap * 2 : 65536;
        while (c < a->len + n + 1) c *= 2;
        char * p = (char *) realloc(a->out, c);
        if (!p) return;
        a->out = p; a->cap = c;
    }
    memcpy(a->out + a->len, s, n + 1);
    a->len += n;
}

static void on_progress(struct whisper_context * ctx, struct whisper_state * st, int p, void * user) {
    (void) ctx; (void) st;
    aw * a = (aw *) user;
    if (a->progress) a->progress(p);
}

static bool on_abort(void * user) {
    aw * a = (aw *) user;
    return a->stop && a->stop() != 0;
}

static void quiet(enum ggml_log_level level, const char * text, void * user) { (void) level; (void) text; (void) user; }

AW_API const char * aw_version(void) { return "aura_whisper 1 / whisper.cpp 1.7.6"; }

// dtw: which alignment-heads preset matches the model (whisper.h's WHISPER_AHEADS_*), 0 for none
AW_API void * aw_open(const char * model_path, int dtw) {
    whisper_log_set(quiet, NULL);
    struct whisper_context_params cp = whisper_context_default_params();
    cp.use_gpu = false;
    if (dtw > 0) { cp.dtw_token_timestamps = true; cp.dtw_aheads_preset = (enum whisper_alignment_heads_preset) dtw; }
    struct whisper_context * ctx = whisper_init_from_file_with_params(model_path, cp);
    if (!ctx && dtw > 0) { cp.dtw_token_timestamps = false; ctx = whisper_init_from_file_with_params(model_path, cp); }
    if (!ctx) return NULL;
    aw * a = (aw *) calloc(1, sizeof(aw));
    a->ctx = ctx;
    return a;
}

AW_API void aw_close(void * h) {
    aw * a = (aw *) h;
    if (!a) return;
    whisper_free(a->ctx);
    free(a->out);
    free(a);
}

// samples: mono, 16 kHz. lang: "en", "auto", ...  prompt: text to steer it (the lyrics, or the song's title).
// beam: 1 = quickest. Returns 0 when it worked. The result (aw_result) has one line per token:
//   segment \t start \t end \t dtw \t probability \t text
// times in seconds (dtw is -1 when not known); special tokens are left out.
AW_API int aw_run(void * h, const float * samples, int n, const char * lang, const char * prompt, int beam, int threads,
                  aw_progress progress, aw_stop stop) {
    aw * a = (aw *) h;
    if (!a) return -1;
    a->len = 0; if (a->out) a->out[0] = 0;
    a->progress = progress; a->stop = stop;
    struct whisper_full_params p = whisper_full_default_params(beam > 1 ? WHISPER_SAMPLING_BEAM_SEARCH : WHISPER_SAMPLING_GREEDY);
    p.n_threads = threads > 0 ? threads : 4;
    p.print_progress = false; p.print_realtime = false; p.print_timestamps = false; p.print_special = false;
    p.token_timestamps = true;
    p.no_context = true;
    p.suppress_blank = true;
    p.language = (lang && *lang) ? lang : "auto";
    p.detect_language = false;
    p.initial_prompt = (prompt && *prompt) ? prompt : NULL;
    if (beam > 1) { p.beam_search.beam_size = beam; }
    p.progress_callback = on_progress; p.progress_callback_user_data = a;
    p.abort_callback = on_abort; p.abort_callback_user_data = a;
    int rc = whisper_full(a->ctx, p, samples, n);
    if (rc != 0) return rc;
    int lid = whisper_full_lang_id(a->ctx);
    const char * ls = lid >= 0 ? whisper_lang_str(lid) : "";
    strncpy(a->lang, ls ? ls : "", sizeof(a->lang) - 1);
    whisper_token eot = whisper_token_eot(a->ctx);
    int ns = whisper_full_n_segments(a->ctx);
    char line[1024];
    for (int i = 0; i < ns; i++) {
        int nt = whisper_full_n_tokens(a->ctx, i);
        for (int j = 0; j < nt; j++) {
            whisper_token_data d = whisper_full_get_token_data(a->ctx, i, j);
            if (d.id >= eot) continue;
            const char * txt = whisper_full_get_token_text(a->ctx, i, j);
            char clean[600]; int k = 0;
            for (const char * c = txt; *c && k < 598; c++) clean[k++] = (*c == '\t' || *c == '\n' || *c == '\r') ? ' ' : *c;
            clean[k] = 0;
            snprintf(line, sizeof line, "%d\t%.2f\t%.2f\t%.2f\t%.3f\t%s\n", i, d.t0 / 100.0, d.t1 / 100.0, d.t_dtw >= 0 ? d.t_dtw / 100.0 : -1.0, d.p, clean);
            put(a, line);
        }
    }
    return 0;
}

AW_API const char * aw_result(void * h) { aw * a = (aw *) h; return a && a->out ? a->out : ""; }
AW_API const char * aw_language(void * h) { aw * a = (aw *) h; return a ? a->lang : ""; }
