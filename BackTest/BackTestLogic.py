import pandas as pd
import numpy as np
import yfinance as yf
from datetime import datetime
import warnings
warnings.filterwarnings('ignore')

def run_momentum_backtest(file_path, max_positions=5):
    print(f"1. Loading Watchlist from: {file_path}")
    default_cols = ['Ticker', 'Entry Date', 'Entry Price', 'Exit Date', 'Exit Price', 'Shares', 'PnL', 'Return (%)', 'Reason', 'Total Equity']

    try:
        df_input = pd.read_excel(file_path)
        symbol_col = next((c for c in df_input.columns if 'symbol' in str(c).lower()), None)

        if symbol_col:
            raw_symbols = df_input[symbol_col].dropna().unique().tolist()
            symbols = [str(s).strip() for s in raw_symbols if isinstance(s, str) and not str(s).replace('.', '', 1).isdigit() and str(s).upper() != 'LTP']
            tickers = [f"{sym}.NS" for sym in symbols]
        else:
            print(f"Could not find a 'symbol' column. Available columns: {df_input.columns.tolist()}")
            return pd.DataFrame(columns=default_cols)
    except Exception as e:
        print(f"Error reading Excel file: {e}")
        return pd.DataFrame(columns=default_cols)

    print(f"2. Fetching historical data (2020 to Present)... {len(tickers)} tickers found.")
    hist_data = {}
    valid_tickers = []
    current_date_str = datetime.now().strftime('%Y-%m-%d')

    for ticker in tickers:
        df = yf.download(ticker, start="2020-01-01", end=current_date_str, progress=False)
        if df.empty or len(df) < 50: continue
        if isinstance(df.columns, pd.MultiIndex): df.columns = df.columns.droplevel(1)
        df.dropna(inplace=True)

        df['EMA10'] = df['Close'].ewm(span=10, adjust=False).mean()
        df['EMA20'] = df['Close'].ewm(span=20, adjust=False).mean()
        df['Vol_SMA20'] = df['Volume'].rolling(window=20).mean()
        df['RVOL'] = df['Volume'] / df['Vol_SMA20']
        df['Cross_Up'] = (df['EMA10'] > df['EMA20']) & (df['EMA10'].shift(1) <= df['EMA20'].shift(1))
        df['Cross_Down'] = (df['EMA10'] < df['EMA20']) & (df['EMA10'].shift(1) >= df['EMA20'].shift(1))

        # --- NEW ATR MATH ---
        high_low = df['High'] - df['Low']
        high_close = np.abs(df['High'] - df['Close'].shift())
        low_close = np.abs(df['Low'] - df['Close'].shift())
        ranges = pd.concat([high_low, high_close, low_close], axis=1)
        true_range = np.max(ranges, axis=1)
        df['ATR10'] = true_range.rolling(10).mean()

        hist_data[ticker] = df
        valid_tickers.append(ticker)

    print(f"3. Running Backtest (Max Positions: {max_positions})...")
    starting_capital = 1500000
    cash = starting_capital
    open_positions = {}
    trade_log = []
    all_dates = sorted(list(set.union(*[set(hist_data[t].index) for t in valid_tickers])))

    for current_date in all_dates:
        # Handle Exits
        for ticker in list(open_positions.keys()):
            df = hist_data[ticker]
            if current_date not in df.index: continue
            today, pos = df.loc[current_date], open_positions[ticker]
            exit_price, reason = None, ""

            # The ATR Hard Stop
            hard_sl = pos['stop_loss']

            if today['Low'] <= hard_sl:
                # min() simulates slippage if it gaps down below your stop
                exit_price, reason = min(today['Open'], hard_sl), "2x ATR Stop Hit"
            elif today['Cross_Down']:
                exit_price, reason = today['Close'], "EMA Cross Down"

            if exit_price:
                pnl = (exit_price - pos['entry_price']) * pos['shares']
                cash += (exit_price * pos['shares'])
                del open_positions[ticker]

                open_val = sum([hist_data[t].loc[current_date, 'Close'] * p['shares'] if current_date in hist_data[t].index else hist_data[t].loc[:current_date].iloc[-1]['Close'] * p['shares'] for t, p in open_positions.items()])

                total_val = cash + open_val
                trade_log.append({'Ticker': ticker, 'Entry Date': pos['entry_date'], 'Entry Price': pos['entry_price'], 'Exit Date': current_date, 'Exit Price': exit_price, 'Shares': pos['shares'], 'PnL': pnl, 'Return (%)': round(((exit_price/pos['entry_price'])-1)*100, 2), 'Reason': reason, 'Total Equity': round(total_val, 2)})

        # Handle Entries
        if len(open_positions) < max_positions:
            current_open_val = sum([hist_data[t].loc[current_date, 'Close'] * p['shares'] if current_date in hist_data[t].index else hist_data[t].loc[:current_date].iloc[-1]['Close'] * p['shares'] for t, p in open_positions.items()])

            current_total_equity = cash + current_open_val
            dynamic_alloc = current_total_equity / max_positions
            candidates = []

            for t in valid_tickers:
                if t in open_positions or current_date not in hist_data[t].index: continue
                row = hist_data[t].loc[current_date]

                # Added ATR Check to filters
                if row['Cross_Up'] and row['Close'] > row['EMA10'] and pd.notna(row['RVOL']) and pd.notna(row['ATR10']):
                    candidates.append({'Ticker': t, 'Close': row['Close'], 'RVOL': row['RVOL'], 'ATR10': row['ATR10']})

            for cand in sorted(candidates, key=lambda x: x['RVOL'], reverse=True)[:(max_positions - len(open_positions))]:
                shares = int(dynamic_alloc / cand['Close'])
                if shares > 0 and cash >= (shares * cand['Close']):
                    cash -= (shares * cand['Close'])
                    # Calculate and store the 2x ATR stop loss here
                    open_positions[cand['Ticker']] = {'shares': shares, 'entry_price': cand['Close'], 'stop_loss': cand['Close'] - (2 * cand['ATR10']), 'entry_date': current_date}

    results_df = pd.DataFrame(trade_log)
    results_df.to_csv('backtest_results_dynamic.csv', index=False)

    # Final Portfolio Value Calculation
    open_val_final = 0
    for t, p in open_positions.items():
        open_val_final += hist_data[t].iloc[-1]['Close'] * p['shares']

    final_portfolio_val = cash + open_val_final
    print(f"\nBacktest Complete ({max_positions} Slots). Final Equity: {final_portfolio_val:.2f}")
    print(f"Total Return: {((final_portfolio_val/starting_capital)-1)*100:.2f}%")
    display(results_df.tail())
    return results_df

# Execute with 5 max positions to align with the ₹3L per trade (on ₹15L capital) strategy
trade_history = run_momentum_backtest('/content/Momentum stocks - Index.xlsx', max_positions=5)