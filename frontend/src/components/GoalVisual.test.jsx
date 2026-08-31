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
});
