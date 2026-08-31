import { render, screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { GoalVisual, goalVisuals } from './GoalVisual';

describe('Finova goal visual library', () => {
    it('provides 24 unique, searchable household goal visuals', () => {
        expect(goalVisuals).toHaveLength(24);
        expect(new Set(goalVisuals.map((item) => item.key)).size).toBe(24);
        goalVisuals.forEach((item) => {
            expect(item.title.length).toBeGreaterThan(2);
            expect(item.category.length).toBeGreaterThan(2);
            expect(item.searchTerms.length).toBeGreaterThan(0);
        });
    });

    it('renders a labelled themeable goal visual', () => {
        render(
            <GoalVisual
                iconKey="house_deposit"
                colorKey="mint"
                label="Our house deposit"
            />
        );
        expect(
            screen.getByRole('img', { name: 'Our house deposit' })
        ).toBeInTheDocument();
    });

    it('falls back safely when an old icon key is returned', () => {
        render(<GoalVisual iconKey="retired-key" label="Fallback goal" />);
        expect(
            screen.getByRole('img', { name: 'Fallback goal' })
        ).toBeInTheDocument();
    });

    it('renders same-origin goal images', () => {
        render(
            <GoalVisual imageUrl="/api/goals/images/7" label="Uploaded goal" />
        );

        const image = screen.getByRole('img', { name: 'Uploaded goal' });
        expect(image.tagName).toBe('IMG');
        expect(image).toHaveAttribute(
            'src',
            new URL('/api/goals/images/7', window.location.origin).href
        );
    });

    it.each([
        'javascript:alert(document.domain)',
        'data:image/svg+xml,<svg onload="alert(1)" />',
        'https://example.com/tracker.png',
        'blob:https://example.com/tracker-id',
        'http://[invalid',
    ])('rejects unsafe image URL %s', (imageUrl) => {
        const { container } = render(
            <GoalVisual imageUrl={imageUrl} label="Safe fallback" />
        );

        const fallback = container.querySelector('[role="img"]');
        expect(fallback.tagName).toBe('DIV');
        expect(fallback).toHaveAttribute('aria-label', 'Safe fallback');
        expect(fallback).not.toHaveAttribute('src');
    });
});
