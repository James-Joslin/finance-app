import { createElement } from 'react';
import { act, render } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { parseRecordId, useDeepLinkTarget } from './deepLink';

function DeepLinkHarness({ targetId, ready = true }) {
    useDeepLinkTarget(targetId, ready, '[data-deep-link-type="test"]');
    return null;
}

describe('deep-link utilities', () => {
    afterEach(() => {
        vi.useRealTimers();
        document.body.innerHTML = '';
    });

    it('accepts only positive integer record IDs', () => {
        expect(parseRecordId('42')).toBe(42);
        expect(parseRecordId('0')).toBeNull();
        expect(parseRecordId('nope')).toBeNull();
    });

    it('scrolls to the target, highlights it, and removes the highlight', () => {
        vi.useFakeTimers();
        const target = document.createElement('article');
        target.dataset.deepLinkType = 'test';
        target.dataset.deepLinkId = '42';
        target.scrollIntoView = vi.fn();
        document.body.append(target);

        render(createElement(DeepLinkHarness, { targetId: 42 }));

        expect(target.scrollIntoView).toHaveBeenCalledWith({
            behavior: 'smooth',
            block: 'center',
        });
        expect(target).toHaveClass('deep-link-highlight');

        act(() => vi.advanceTimersByTime(1800));
        expect(target).not.toHaveClass('deep-link-highlight');
    });

    it('does nothing until the destination data is ready', () => {
        const target = document.createElement('article');
        target.dataset.deepLinkType = 'test';
        target.dataset.deepLinkId = '42';
        target.scrollIntoView = vi.fn();
        document.body.append(target);

        render(createElement(DeepLinkHarness, { targetId: 42, ready: false }));

        expect(target.scrollIntoView).not.toHaveBeenCalled();
        expect(target).not.toHaveClass('deep-link-highlight');
    });
});
