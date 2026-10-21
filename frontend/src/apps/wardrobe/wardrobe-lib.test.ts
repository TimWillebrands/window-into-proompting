import { describe, expect, it } from 'vitest';
import type { ShopItem } from '../../api/model/shopItem';
import {
    inTab,
    isService,
    itemColour,
    itemIcon,
    toggleTry,
    tryOn,
} from './wardrobe-lib';

const hoodie: ShopItem = {
    key: 'top:hoodie:navy',
    slot: 'top',
    name: 'a navy hoodie',
    price: 30,
    picks: { topStyle: 'hoodie', top: 'navy' },
};
const tee: ShopItem = {
    key: 'top:tshirt:red',
    slot: 'top',
    name: 'a red t-shirt',
    price: 10,
    picks: { topStyle: 'tshirt', top: 'red' },
};
const cap: ShopItem = {
    key: 'accessory:cap',
    slot: 'accessory',
    name: 'a cap',
    price: 12,
    picks: { accessory: 'cap' },
};
const cut: ShopItem = {
    key: 'hairStyle:bob',
    slot: 'hairStyle',
    name: 'a bob',
    price: 25,
    picks: { hairStyle: 'bob' },
};

describe('wardrobe-lib', () => {
    it('tries things on over the current look, one per slot', () => {
        let tried = toggleTry([], hoodie);
        tried = toggleTry(tried, cap);
        tried = toggleTry(tried, tee);
        expect(tried.map((t) => t.key)).toEqual([
            'accessory:cap',
            'top:tshirt:red',
        ]);
        const look = tryOn(
            { hair: 'black', topStyle: 'suit', top: 'grey', accessory: 'none' },
            tried,
        );
        expect(look).toMatchObject({
            hair: 'black',
            topStyle: 'tshirt',
            top: 'red',
            accessory: 'cap',
        });
        expect(toggleTry(tried, cap).map((t) => t.key)).toEqual([
            'top:tshirt:red',
        ]);
    });

    it('draws tiles from what an item is', () => {
        expect(itemIcon(hoodie)).toBe('🧥');
        expect(itemIcon(cap)).toBe('🧢');
        expect(itemColour(hoodie)).toBe('#2c3e66');
        expect(isService(cut)).toBe(true);
        expect(inTab(cut, 'hair')).toBe(true);
        expect(inTab(cut, 'top')).toBe(false);
        expect(inTab(cap, 'all')).toBe(true);
    });
});
