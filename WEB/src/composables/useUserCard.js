import { userApi } from "services/api";

// One request per user for the life of the page: the same few people are hovered again and again.
const cards = new Map();

export const loadUserCard = (id) => {
  if (!cards.has(id)) {
    cards.set(id, userApi.card(id).catch((err) => {
      cards.delete(id);
      throw err;
    }));
  }
  return cards.get(id);
};
