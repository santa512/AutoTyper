#include "TypingEngine.h"

#include <algorithm>
#include <cmath>
#include <cwctype>
#include <stdexcept>

namespace compact {

TypingEngine::TypingEngine(std::wstring text, Options options, std::uint32_t seed)
    : source_(std::move(text)), options_(options), random_(seed) {
    if (!std::isfinite(options.pace) || options.pace < 0.5 || options.pace > 2.0 ||
        !std::isfinite(options.rhythm) || options.rhythm < 0 || options.rhythm > 1 ||
        !std::isfinite(options.mistakeChance) || options.mistakeChance < 0 || options.mistakeChance > 1) {
        throw std::invalid_argument("Invalid typing settings.");
    }
    for (std::size_t i = 0; i < source_.size();) {
        const std::size_t begin = i++;
        if (source_[begin] >= 0xD800 && source_[begin] <= 0xDBFF && i < source_.size() &&
            source_[i] >= 0xDC00 && source_[i] <= 0xDFFF) {
            ++i;
        }
        tokens_.push_back({source_.substr(begin, i - begin), begin});
    }
}

int TypingEngine::between(int low, int high) {
    return std::uniform_int_distribution<int>(low, high)(random_);
}

double TypingEngine::chance() {
    return std::generate_canonical<double, 32>(random_);
}

int TypingEngine::scaleDelay(int milliseconds) {
    if (groupRemaining_ == 0) {
        groupRemaining_ = between(3, 7);
        groupScale_ = 1 + (chance() * 2 - 1) * 0.35 * options_.rhythm;
    }
    --groupRemaining_;
    const int jitter = static_cast<int>(milliseconds * 0.12 * options_.rhythm);
    int delay = static_cast<int>(milliseconds * groupScale_) + between(-jitter, jitter);
    if (chance() < options_.rhythm * 0.025) delay += between(180, 450);
    return std::clamp(static_cast<int>(delay / options_.pace), 15, 1200);
}

wchar_t TypingEngine::nearby(wchar_t letter) {
    const std::wstring rows[] = {L"qwertyuiop", L"asdfghjkl", L"zxcvbnm"};
    const double offsets[] = {0, 0.25, 0.75};
    const wchar_t lower = static_cast<wchar_t>(std::towlower(letter));
    double keyX = 0;
    int keyY = -1;
    for (int row = 0; row < 3; ++row) {
        const auto column = rows[row].find(lower);
        if (column != std::wstring::npos) {
            keyX = static_cast<double>(column) + offsets[row];
            keyY = row;
            break;
        }
    }
    if (keyY < 0) return letter;
    std::wstring neighbors;
    for (int row = 0; row < 3; ++row) {
        for (std::size_t column = 0; column < rows[row].size(); ++column) {
            const double dx = static_cast<double>(column) + offsets[row] - keyX;
            const int dy = row - keyY;
            if (rows[row][column] != lower && dx * dx + dy * dy <= 1.65) {
                neighbors.push_back(rows[row][column]);
            }
        }
    }
    if (neighbors.empty()) return letter;
    const wchar_t result = neighbors[static_cast<std::size_t>(between(0, static_cast<int>(neighbors.size()) - 1))];
    return std::iswupper(letter) ? static_cast<wchar_t>(std::towupper(result)) : result;
}

std::size_t TypingEngine::progress() const {
    return nextToken_ < tokens_.size() ? tokens_[nextToken_].offset : source_.size();
}

void TypingEngine::queueCorrection(const Correction& correction) {
    const auto& token = tokens_[correction.position];
    const auto completed = progress();
    queued_.push_back({Action::Wait, {}, between(900, 1800), token.offset, completed, L"Noticed a typo"});
    while (caret_ > correction.position + 1) {
        queued_.push_back({Action::Left, {}, scaleDelay(100), token.offset, completed, L"Finding typo"});
        --caret_;
    }
    queued_.push_back({Action::Wait, {}, between(250, 600), token.offset, completed, L"Fixing typo"});
    queued_.push_back({Action::Backspace, {}, scaleDelay(100), token.offset, completed, L"Backspace"});
    queued_.push_back({Action::Text, token.text, scaleDelay(100), token.offset, completed, L"Correcting typo"});
    while (caret_ < typedLength_) {
        queued_.push_back({Action::Right, {}, scaleDelay(100), token.offset, completed, L"Returning to text"});
        ++caret_;
    }
}

std::optional<Step> TypingEngine::next() {
    if (queued_.empty()) {
        const auto due = std::find_if(corrections_.begin(), corrections_.end(), [this](const Correction& item) {
            return item.due <= nextToken_;
        });
        if (due != corrections_.end() || (nextToken_ == tokens_.size() && !corrections_.empty())) {
            const auto found = due != corrections_.end() ? due : corrections_.begin();
            const auto correction = *found;
            corrections_.erase(found);
            queueCorrection(correction);
        } else if (nextToken_ < tokens_.size()) {
            const auto& token = tokens_[nextToken_];
            std::wstring output = token.text;
            const wchar_t character = output.front();
            int delay = 75;
            if (std::iswpunct(character)) delay += between(100, 260);
            else if (std::iswspace(character)) delay += between(15, 70);
            const bool asciiLetter = (character >= L'a' && character <= L'z') ||
                                     (character >= L'A' && character <= L'Z');
            const bool typo = asciiLetter && chance() < options_.mistakeChance;
            if (typo) {
                output[0] = nearby(character);
                const std::size_t extra = chance() < 0.7 ? 1 : static_cast<std::size_t>(between(4, 9));
                corrections_.push_back({nextToken_, nextToken_ + 1 + extra});
            }
            ++nextToken_;
            ++caret_;
            ++typedLength_;
            queued_.push_back({Action::Text, std::move(output), scaleDelay(delay), token.offset, progress(),
                               typo ? L"Small typo" : L"Typing"});
        } else {
            return std::nullopt;
        }
    }
    Step result = std::move(queued_.front());
    queued_.pop_front();
    return result;
}

} // namespace compact
