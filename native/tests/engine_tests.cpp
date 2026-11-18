#include "TypingEngine.h"
#include <iostream>
#include <stdexcept>
#include <vector>

namespace {
void require(bool condition, const char* reason) {
    if (!condition) throw std::runtime_error(reason);
}

struct Result { std::wstring text; int corrections = 0; int arrows = 0; std::size_t progress = 0; };

Result simulate(const std::wstring& source, compact::Options options, std::uint32_t seed) {
    compact::TypingEngine engine(source, options, seed);
    std::vector<std::wstring> buffer;
    std::size_t caret = 0, previousProgress = 0;
    Result result;
    int count = 0;
    while (const auto step = engine.next()) {
        require(++count < 100000, "Engine did not finish");
        require(step->delayMs >= 15 && step->delayMs <= 1800, "Invalid step delay");
        require(step->progress >= previousProgress && step->progress <= source.size(), "Progress went backwards or beyond source");
        require(step->highlightOffset < source.size(), "Highlight outside source");
        previousProgress = step->progress;
        using compact::Action;
        switch (step->action) {
        case Action::Text:
            require(!step->text.empty(), "Empty text action");
            buffer.insert(buffer.begin() + static_cast<std::ptrdiff_t>(caret), step->text); ++caret;
            break;
        case Action::Left:
            require(caret > 0, "Left arrow before beginning"); --caret; ++result.arrows; break;
        case Action::Right:
            require(caret < buffer.size(), "Right arrow past end"); ++caret; ++result.arrows; break;
        case Action::Backspace:
            require(caret > 0, "Backspace before beginning");
            buffer.erase(buffer.begin() + static_cast<std::ptrdiff_t>(--caret)); ++result.corrections; break;
        case Action::Wait: break;
        }
    }
    require(caret == buffer.size(), "Caret did not return to end");
    for (const auto& token : buffer) result.text += token;
    result.progress = previousProgress;
    require(result.text == source, "Final output differs from source");
    require(result.progress == source.size(), "Final progress is not complete");
    return result;
}
} // namespace

int main() {
    try {
        compact::TypingEngine empty(L"", {}, 1);
        require(!empty.next(), "Empty input produced an action");
        compact::Options options; options.mistakeChance = 0;
        const auto clean = simulate(L"Hello, World!\nTabs\there. 123", options, 3);
        require(clean.corrections == 0 && clean.arrows == 0, "Zero mistakes produced corrections");
        options.mistakeChance = 1;
        bool delayed = false;
        for (std::uint32_t seed = 0; seed < 50; ++seed) {
            const auto result = simulate(L"The QUICK brown fox jumps over the lazy dog.\nAll letters recover.", options, seed);
            require(result.corrections > 0, "Forced mistakes did not occur");
            delayed = delayed || result.arrows > 0;
        }
        require(delayed, "Delayed corrections were not exercised");
        options.mistakeChance = 0.5;
        simulate(L"A\xD83D\xDE00Z\nCafe\x00E9 and \x03A9.", options, 101);
        options.pace = 2; options.rhythm = 0;
        simulate(L"Faster punctuation, text!", options, 12);
        options.pace = 0.5; options.rhythm = 1;
        simulate(L"Slow rhythm with mistakes.", options, 7);
        bool rejected = false;
        try { options.pace = 0; compact::TypingEngine invalid(L"test", options, 0); }
        catch (const std::invalid_argument&) { rejected = true; }
        require(rejected, "Invalid options were accepted");
        std::cout << "PASS: empty input, exact output, zero/forced/random typos, delayed navigation, Unicode, timing, invalid settings\n";
        return 0;
    } catch (const std::exception& error) {
        std::cerr << "FAIL: " << error.what() << '\n'; return 1;
    }
}
