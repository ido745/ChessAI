# ♟️ Chess AI Project

A modern and efficient **Chess Engine built in Unity**, combining high-performance **bitboard move generation** with an advanced **Negamax + Alpha-Beta pruning** search algorithm.

---

## ⚙️ Technical Overview

This project is made with **Unity** and implements:
- A **fast, modern move generator** using [Magic Bitboards](https://www.chessprogramming.org/Magic_Bitboards)
- An optimized **Negamax** algorithm with **Alpha-Beta pruning**
- Advanced search heuristics to achieve **deep and efficient searches**
- A full visual interface for playing against the AI or a friend

---

## 🧩 Project Structure

The scripts in the project are divided into three main modules:

### 🎨 Visual Layer
Handles the graphical representation of the board and pieces:
- Drag-and-drop piece movement
- Visual updates and UI elements (buttons, text, etc.)
- Synchronization with the logical board state

### 🧠 Logic Layer
Manages all internal calculations, including:
- Move generation using **Magic Bitboards**
- **Zobrist hashing** for board state identification
- Move simulation (without display) for AI analysis

### 🤖 AI Layer
Responsible for searching positions and selecting the best move within a given time limit:
- Calls logic functions to simulate moves
- Updates visuals once a move is chosen

---

## ⚡ Move Generation

Move generation is implemented with the efficient **Magic Bitboards** technique, enabling O(1) lookup for sliding piece attacks.

> 🧠 *Magic Bitboards are a method to transform board occupancy into fast, pre-computed attack sets using bitwise operations and “magic” hash multipliers.*  
Learn more at: [ChessProgramming.org/Magic_Bitboards](https://www.chessprogramming.org/Magic_Bitboards)

---

## 🧮 AI Algorithm

The AI engine is based on a **Negamax** search (a simplified variant of Minimax) with **Alpha-Beta pruning** and many modern enhancements:

### 🔍 Core Techniques
- **Negamax + Alpha-Beta pruning** for efficient minimization of search branches  
- **Quiescence Search** for tactical stability  
- **Iterative Deepening** to ensure best-move convergence under time limits  

### 🚀 Optimizations
- **Transposition Table** for caching previously evaluated positions  
- **Evaluation caching** for reusing scores  
- **Principal Variation (PV) Search** for move ordering  
- **Aspiration Windows** for faster convergence  
- **Null Move Pruning** and **Late Move Reductions (LMR)** for deeper searches  
- **Killer Move Heuristic** and **History Heuristic** for dynamic move ordering  

### 🧩 Evaluation Function
The evaluation function considers:
- Material balance  
- Piece activity and positioning  
- **King safety**  
- **Pawn structure** (isolated, doubled, passed pawns)  
- **Bishop pair advantage**  
- **Connected rooks**, **open files**, and **control of the center**  

---

## 🕹️ Game Features

The engine includes:
- **Time control settings** (Bullet, Blitz, Rapid, etc.)
- **Color selection menu** – choose to play as **White**, **Black**, or **Random**
- **Play modes** – against **AI** or a **friend**
- Real-time info display: AI **search depth**, **evaluation**, and **played line**
